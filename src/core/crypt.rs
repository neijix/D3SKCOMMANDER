//! File encryption and the password store.
//!
//! Files use the [age](https://age-encryption.org) format with a passphrase (scrypt
//! recipient): a binary stream, so `<name>.age` files open with any age tool. The
//! password store is a small age-encrypted JSON document protected by a master password.
//! Passwords are never logged or put into error messages.

use std::collections::BTreeMap;
use std::fs::{self, File, OpenOptions};
use std::io::{self, BufReader, BufWriter, Read, Write};
use std::iter;
use std::path::{Path, PathBuf};

use age::secrecy::{ExposeSecret, SecretString};
use age::{DecryptError, Decryptor, Encryptor, Identity, Recipient, scrypt};
use serde::{Deserialize, Serialize};

const CHUNK: usize = 64 * 1024;
const AGE_EXTENSION: &str = "age";

/// `<src>.age`, the default destination of [`encrypt_file`].
pub fn default_encrypted_path(src: &Path) -> PathBuf {
    let mut name = src.file_name().unwrap_or_default().to_os_string();
    name.push(".");
    name.push(AGE_EXTENSION);
    src.with_file_name(name)
}

/// The default destination of [`decrypt_file`]: `<src>` without its `.age` extension, or
/// `<src>.decrypted` when it has none.
pub fn default_decrypted_path(src: &Path) -> PathBuf {
    let is_age = src
        .extension()
        .is_some_and(|e| e.eq_ignore_ascii_case(AGE_EXTENSION));
    if is_age {
        return src.with_extension("");
    }
    let mut name = src.file_name().unwrap_or_default().to_os_string();
    name.push(".decrypted");
    src.with_file_name(name)
}

fn passphrase(password: &str) -> SecretString {
    SecretString::from(password.to_owned())
}

fn cancelled() -> io::Error {
    io::Error::new(io::ErrorKind::Interrupted, "cancelled")
}

/// Run `body` against a temporary file next to `dest`, then rename it over `dest`. On any
/// error the temporary file is removed and `dest` is left untouched. `private` creates the
/// file readable by its owner only (Unix mode 0600).
fn write_atomically(
    dest: &Path,
    private: bool,
    body: impl FnOnce(&mut BufWriter<File>) -> io::Result<()>,
) -> io::Result<()> {
    let mut tmp_name = std::ffi::OsString::from(".");
    tmp_name.push(dest.file_name().unwrap_or_default());
    tmp_name.push(".tmp");
    let tmp = dest.with_file_name(tmp_name);
    let result = write_tmp(&tmp, private, body).and_then(|()| fs::rename(&tmp, dest));
    if result.is_err() {
        let _ = fs::remove_file(&tmp);
    }
    result
}

fn write_tmp(
    tmp: &Path,
    private: bool,
    body: impl FnOnce(&mut BufWriter<File>) -> io::Result<()>,
) -> io::Result<()> {
    let mut options = OpenOptions::new();
    options.write(true).create(true).truncate(true);
    #[cfg(unix)]
    if private {
        use std::os::unix::fs::OpenOptionsExt;
        options.mode(0o600);
    }
    let file = options.open(tmp)?;
    #[cfg(unix)]
    if private {
        use std::os::unix::fs::PermissionsExt;
        // A stale temporary file keeps its old mode: tighten it explicitly.
        file.set_permissions(fs::Permissions::from_mode(0o600))?;
    }
    #[cfg(not(unix))]
    let _ = private;
    let mut writer = BufWriter::new(file);
    body(&mut writer)?;
    writer.flush()?;
    writer.get_ref().sync_all()
}

fn encrypt_stream<R: Read, W: Write>(
    input: &mut R,
    output: W,
    password: &str,
    work_factor: Option<u8>,
    progress: &mut dyn FnMut(u64) -> bool,
) -> io::Result<W> {
    let mut recipient = scrypt::Recipient::new(passphrase(password));
    if let Some(log_n) = work_factor {
        recipient.set_work_factor(log_n);
    }
    let encryptor = Encryptor::with_recipients(iter::once(&recipient as &dyn Recipient))
        .map_err(|e| io::Error::other(e.to_string()))?;
    let mut writer = encryptor.wrap_output(output)?;
    let mut buf = vec![0u8; CHUNK];
    let mut done = 0u64;
    loop {
        let n = input.read(&mut buf)?;
        if n == 0 {
            break;
        }
        writer.write_all(&buf[..n])?;
        done += n as u64;
        if !progress(done) {
            return Err(cancelled());
        }
    }
    writer.finish()
}

fn decrypt_error(err: DecryptError) -> io::Error {
    match err {
        DecryptError::DecryptionFailed
        | DecryptError::NoMatchingKeys
        | DecryptError::KeyDecryptionFailed => {
            io::Error::new(io::ErrorKind::PermissionDenied, "wrong password")
        }
        DecryptError::Io(e) => e,
        other => io::Error::new(
            io::ErrorKind::InvalidData,
            format!("not a valid encrypted file: {other}"),
        ),
    }
}

fn decrypt_stream<R: io::BufRead, W: Write>(
    input: R,
    output: &mut W,
    password: &str,
    progress: &mut dyn FnMut(u64) -> bool,
) -> io::Result<()> {
    let decryptor = Decryptor::new_buffered(input).map_err(decrypt_error)?;
    let identity = scrypt::Identity::new(passphrase(password));
    let mut reader = decryptor
        .decrypt(iter::once(&identity as &dyn Identity))
        .map_err(decrypt_error)?;
    let mut buf = vec![0u8; CHUNK];
    let mut done = 0u64;
    loop {
        let n = reader.read(&mut buf)?;
        if n == 0 {
            return Ok(());
        }
        output.write_all(&buf[..n])?;
        done += n as u64;
        if !progress(done) {
            return Err(cancelled());
        }
    }
}

/// Encrypt `src` into `dest` (age, passphrase mode). `progress` receives the number of
/// plaintext bytes processed; returning `false` cancels (`ErrorKind::Interrupted`). `dest`
/// is written through a temporary file, so a failure never leaves a partial file.
pub fn encrypt_file(
    src: &Path,
    dest: &Path,
    password: &str,
    progress: &mut dyn FnMut(u64) -> bool,
) -> io::Result<()> {
    encrypt_file_with(src, dest, password, None, progress)
}

fn encrypt_file_with(
    src: &Path,
    dest: &Path,
    password: &str,
    work_factor: Option<u8>,
    progress: &mut dyn FnMut(u64) -> bool,
) -> io::Result<()> {
    let mut input = File::open(src)?;
    write_atomically(dest, false, |out| {
        encrypt_stream(&mut input, out, password, work_factor, progress).map(|_| ())
    })
}

/// Decrypt `src` into `dest`. A wrong password (or a file not encrypted with a password)
/// is `ErrorKind::PermissionDenied` with the message "wrong password"; a file that is not
/// an age file at all is `ErrorKind::InvalidData`. Nothing is written to `dest` on failure.
pub fn decrypt_file(
    src: &Path,
    dest: &Path,
    password: &str,
    progress: &mut dyn FnMut(u64) -> bool,
) -> io::Result<()> {
    let input = BufReader::new(File::open(src)?);
    write_atomically(dest, false, |out| {
        decrypt_stream(input, out, password, progress)
    })
}

#[derive(Serialize, Deserialize, Default)]
struct StoreFile {
    version: u32,
    entries: BTreeMap<String, String>,
}

/// Named secrets (FTP passwords, …) kept in one age-encrypted file under a master
/// password. Secrets live in memory only while the store is open; dropping it wipes them.
pub struct PasswordStore {
    path: PathBuf,
    master: SecretString,
    entries: BTreeMap<String, SecretString>,
    /// scrypt work factor override (tests only); `None` = about one second per operation.
    work_factor: Option<u8>,
}

impl std::fmt::Debug for PasswordStore {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("PasswordStore")
            .field("path", &self.path)
            .field("entries", &self.entries.len())
            .finish_non_exhaustive()
    }
}

impl PasswordStore {
    /// Unlock the store at `path`. A missing file gives an empty store that `save` will
    /// create. A wrong master password is `ErrorKind::PermissionDenied`.
    pub fn open(path: &Path, master: &str) -> io::Result<Self> {
        let mut store = PasswordStore {
            path: path.to_path_buf(),
            master: passphrase(master),
            entries: BTreeMap::new(),
            work_factor: None,
        };
        let bytes = match fs::read(path) {
            Ok(bytes) => bytes,
            Err(e) if e.kind() == io::ErrorKind::NotFound => return Ok(store),
            Err(e) => return Err(e),
        };
        let mut plain = Vec::new();
        decrypt_stream(&bytes[..], &mut plain, master, &mut |_| true)?;
        let parsed: StoreFile = serde_json::from_slice(&plain)
            .map_err(|_| io::Error::new(io::ErrorKind::InvalidData, "corrupt password store"))?;
        store.entries = parsed
            .entries
            .into_iter()
            .map(|(k, v)| (k, SecretString::from(v)))
            .collect();
        Ok(store)
    }

    pub fn get(&self, key: &str) -> Option<&str> {
        self.entries.get(key).map(|s| s.expose_secret())
    }

    pub fn set(&mut self, key: &str, secret: &str) {
        self.entries.insert(key.to_owned(), passphrase(secret));
    }

    pub fn remove(&mut self, key: &str) {
        self.entries.remove(key);
    }

    /// Stored names, sorted.
    pub fn keys(&self) -> Vec<String> {
        self.entries.keys().cloned().collect()
    }

    /// Encrypt the store with the master password and write it atomically (temporary file
    /// + rename); the file is readable by its owner only on Unix.
    pub fn save(&self) -> io::Result<()> {
        let file = StoreFile {
            version: 1,
            entries: self
                .entries
                .iter()
                .map(|(k, v)| (k.clone(), v.expose_secret().to_owned()))
                .collect(),
        };
        let mut json = serde_json::to_vec(&file).map_err(io::Error::other)?;
        let result = write_atomically(&self.path, true, |out| {
            encrypt_stream(
                &mut &json[..],
                out,
                self.master.expose_secret(),
                self.work_factor,
                &mut |_| true,
            )
            .map(|_| ())
        });
        json.fill(0);
        result
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const FAST: Option<u8> = Some(4);

    fn store_at(path: &Path, master: &str) -> PasswordStore {
        let mut store = PasswordStore::open(path, master).unwrap();
        store.work_factor = FAST;
        store
    }

    #[test]
    fn file_round_trip() {
        let dir = tempfile::tempdir().unwrap();
        let src = dir.path().join("a.bin");
        let data: Vec<u8> = (0..200_000u32).map(|i| (i % 251) as u8).collect();
        fs::write(&src, &data).unwrap();
        let enc = default_encrypted_path(&src);
        assert_eq!(enc, dir.path().join("a.bin.age"));
        let mut last = 0;
        encrypt_file_with(&src, &enc, "pw é", FAST, &mut |n| {
            last = n;
            true
        })
        .unwrap();
        assert_eq!(last, data.len() as u64);
        let cipher = fs::read(&enc).unwrap();
        assert!(cipher.starts_with(b"age-encryption.org/v1"));
        let out = dir.path().join("out.bin");
        decrypt_file(&enc, &out, "pw é", &mut |_| true).unwrap();
        assert_eq!(fs::read(&out).unwrap(), data);
    }

    #[test]
    fn empty_file_round_trip() {
        let dir = tempfile::tempdir().unwrap();
        let src = dir.path().join("e");
        fs::write(&src, b"").unwrap();
        let enc = dir.path().join("e.age");
        encrypt_file_with(&src, &enc, "x", FAST, &mut |_| true).unwrap();
        let out = dir.path().join("e.out");
        decrypt_file(&enc, &out, "x", &mut |_| true).unwrap();
        assert_eq!(fs::read(&out).unwrap(), b"");
    }

    #[test]
    fn wrong_password_is_permission_denied() {
        let dir = tempfile::tempdir().unwrap();
        let src = dir.path().join("a.txt");
        fs::write(&src, b"secret").unwrap();
        let enc = dir.path().join("a.txt.age");
        encrypt_file_with(&src, &enc, "right", FAST, &mut |_| true).unwrap();
        let out = dir.path().join("a.out");
        let err = decrypt_file(&enc, &out, "wrong", &mut |_| true).unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::PermissionDenied);
        assert_eq!(err.to_string(), "wrong password");
        assert!(!out.exists());
        assert!(!dir.path().join(".a.out.tmp").exists());
    }

    #[test]
    fn not_an_age_file_is_invalid_data() {
        let dir = tempfile::tempdir().unwrap();
        let src = dir.path().join("plain.txt");
        fs::write(&src, b"just text, not encrypted").unwrap();
        let err = decrypt_file(&src, &dir.path().join("o"), "x", &mut |_| true).unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::InvalidData);
    }

    #[test]
    fn cancel_leaves_nothing_behind() {
        let dir = tempfile::tempdir().unwrap();
        let src = dir.path().join("big");
        fs::write(&src, vec![7u8; 300_000]).unwrap();
        let enc = dir.path().join("big.age");
        let err = encrypt_file_with(&src, &enc, "x", FAST, &mut |_| false).unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::Interrupted);
        let names: Vec<_> = fs::read_dir(dir.path()).unwrap().collect();
        assert_eq!(names.len(), 1, "only the source remains");
    }

    #[test]
    fn default_paths() {
        let p = Path::new("/x/doc.txt");
        assert_eq!(default_encrypted_path(p), Path::new("/x/doc.txt.age"));
        assert_eq!(
            default_decrypted_path(Path::new("/x/doc.txt.age")),
            Path::new("/x/doc.txt")
        );
        assert_eq!(
            default_decrypted_path(Path::new("/x/blob")),
            Path::new("/x/blob.decrypted")
        );
    }

    #[test]
    fn store_save_open_round_trip() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("store.age");
        let mut store = store_at(&path, "master");
        assert!(store.keys().is_empty());
        store.set("ftp:one", "p1");
        store.set("ftp:two", "pä2");
        store.remove("ftp:one");
        store.set("ftp:three", "p3");
        store.save().unwrap();
        // The file holds no plaintext.
        let raw = fs::read(&path).unwrap();
        assert!(!raw.windows(7).any(|w| w == b"ftp:two"));

        let reopened = PasswordStore::open(&path, "master").unwrap();
        assert_eq!(reopened.keys(), vec!["ftp:three", "ftp:two"]);
        assert_eq!(reopened.get("ftp:two"), Some("pä2"));
        assert_eq!(reopened.get("ftp:one"), None);
        assert!(!format!("{reopened:?}").contains("p3"));
    }

    #[test]
    fn store_wrong_master_fails() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("store.age");
        let mut store = store_at(&path, "master");
        store.set("k", "v");
        store.save().unwrap();
        let err = PasswordStore::open(&path, "other").unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::PermissionDenied);
    }

    #[test]
    fn store_missing_file_is_empty_and_save_overwrites_atomically() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("none.age");
        let mut store = store_at(&path, "m");
        assert!(store.get("k").is_none());
        store.set("k", "v1");
        store.save().unwrap();
        store.set("k", "v2");
        store.save().unwrap();
        assert_eq!(
            PasswordStore::open(&path, "m").unwrap().get("k"),
            Some("v2")
        );
        assert_eq!(fs::read_dir(dir.path()).unwrap().count(), 1, "no temp file");
    }

    #[cfg(unix)]
    #[test]
    fn store_file_is_owner_only() {
        use std::os::unix::fs::PermissionsExt;
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("store.age");
        let mut store = store_at(&path, "m");
        store.set("k", "v");
        store.save().unwrap();
        let mode = fs::metadata(&path).unwrap().permissions().mode();
        assert_eq!(mode & 0o777, 0o600);
    }

    #[test]
    fn store_corrupt_file_is_invalid_data() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("store.age");
        fs::write(&path, b"garbage").unwrap();
        let err = PasswordStore::open(&path, "m").unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::InvalidData);
    }
}
