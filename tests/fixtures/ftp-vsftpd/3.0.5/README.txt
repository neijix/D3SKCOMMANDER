Recorded from vsftpd 3.0.5 (package 3.0.5-0.4) on 2026-11-04.

The server was started as the launching user (`run_as_launching_user=YES`); this shell cannot authenticate sudo.
`force_dot_files=YES` so the leading-dot name appears. The tree mode was 755 so LIST could open it.
Other settings: listen=YES, listen_port=2121, listen_address=127.0.0.1, anonymous_enable=YES,
anon_root=<temp tree>, no_anon_password=YES, local_enable=NO, write_enable=NO, seccomp_sandbox=NO.

Commands (curl LIST, and MLSD which this vsftpd refuses):
  curl -sS --ftp-method nocwd ftp://127.0.0.1:2121/
  curl -sS --ftp-method nocwd ftp://127.0.0.1:2121/alpha/
  curl -sS --ftp-method nocwd ftp://127.0.0.1:2121/beta%20dir/
  curl -v -X MLSD ftp://127.0.0.1:2121/

Tree: alpha/, "beta dir/", empty.dat (0 B), alpha/notes.txt (4 KiB), "beta dir"/sparse.bin (5 GiB sparse),
alpha/"link to notes" -> notes.txt, "été file.txt", "für-ü.txt", .secret, "old report.txt" (2019-03-04).
The temp tree and the config were deleted after the capture.
