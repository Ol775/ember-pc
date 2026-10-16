// The tests share one window and measure the processor: they must not run side by side.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
