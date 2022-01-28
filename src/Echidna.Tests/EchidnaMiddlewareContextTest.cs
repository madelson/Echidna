using Microsoft.Data.SqlClient;

namespace Medallion.Data.Tests;

internal class EchidnaMiddlewareContextTest
{
    [Test]
    public void TestPipeline()
    {
        var middleware = new List<Func<EchidnaMiddlewareContext, ValueTask<object?>>>();
        AddBranchingMiddleware(2);
        AddBranchingMiddleware(3);
        AddBranchingMiddleware(4);

        var context = new InternalContext(middleware);
        var task = context.ExecutePipelineAsync();
        Assert.IsTrue(task.IsCompleted);
        Assert.IsTrue(task.IsCompletedSuccessfully);
        Assert.AreEqual(2 * 3 * 4, task.Result);

        void AddBranchingMiddleware(int count) =>
            middleware.Add(async c =>
            {
                for (var i = 0; i < count - 1; ++i) { await c.NextAsync(); }
                return await c.NextAsync();
            });
    }

    [Test]
    public void TestCanPassDataBetweenMiddleware()
    {
        var middleware = new List<Func<EchidnaMiddlewareContext, ValueTask<object?>>>();
        middleware.Add(async c =>
        {
            c.Data.Add("key1", 5);
            await c.NextAsync();
            return c.Data["key2"];
        });
        middleware.Add(async c =>
        {
            c.Data.Add("key2", 2 * (int)c.Data["key1"]!);
            return await c.NextAsync();
        });

        var context = new InternalContext(middleware);
        var task = context.ExecutePipelineAsync();
        Assert.IsTrue(task.IsCompleted && task.IsCompletedSuccessfully);
        Assert.AreEqual(10, task.Result);
    }

    [Test]
    public async Task TestRetry()
    {
        var middleware = new List<Func<EchidnaMiddlewareContext, ValueTask<object?>>>();
        middleware.Add(async c =>
        {
            var retries = 0;
            while (true)
            {
                try { return await c.NextAsync(); }
                catch when (++retries <= 3) { }
            }
        });

        var context = new InternalContext(middleware) { ShouldThrow = c => c < 4 };
        Assert.AreEqual(4, await context.ExecutePipelineAsync());

        context = new InternalContext(middleware) { ShouldThrow = c => c < 5 };
        Assert.ThrowsAsync<TimeZoneNotFoundException>(() => context.ExecutePipelineAsync().AsTask());
    }

    private class InternalContext : InternalMiddlewareContext
    {
        private int _executeCount;

        public Func<int, bool> ShouldThrow { get; set; } = _ => false;

        public InternalContext(IReadOnlyList<Func<EchidnaMiddlewareContext, ValueTask<object?>>> middleware)
            : base(middleware, new SqlCommand(), isAsync: false)
        {
        }

        protected override ValueTask<object?> InternalExecuteAsync()
        {
            var result = ++this._executeCount;
            return this.ShouldThrow(result) ? throw new TimeZoneNotFoundException() : new(result);
        }
    }
}
