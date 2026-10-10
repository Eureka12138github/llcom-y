using Xunit;

// 测试涉及静态全局状态（Global.setting / AppPaths.ProfilePath），禁用并行以避免相互干扰
[assembly: CollectionBehavior(DisableTestParallelization = true)]