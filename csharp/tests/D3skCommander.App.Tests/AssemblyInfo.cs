using Xunit;

// The shell, tools, dialogs and the screenshot hook share process-wide window and environment state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
