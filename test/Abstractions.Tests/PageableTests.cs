// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Tests;

public class PageableTests
{
    [Fact]
    public void Create_Func_Null_Throws()
    {
        Func<string?, int?, CancellationToken, Task<Page<object>>>? func = null;
        Action act = () => Pageable.Create(func!);
        act.Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public async Task Create_Func_Enumerable()
    {
        CallbackCounter counter = new();
        List<string> expected = CreateSource(15);
        AsyncPageable<string> pageable = CreatePageable(expected, counter);

        List<string> actual = await pageable.ToListAsync();
        actual.Should().BeEquivalentTo(expected);
        counter.Callbacks.Should().Be(5); // 15 / 3 = 5.
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(null, 15)]
    public async Task Create_Func_AsPages(int? start, int? pageSize)
    {
        CallbackCounter counter = new();
        List<string> source = CreateSource(15);
        AsyncPageable<string> pageable = CreatePageable(source, counter);
        List<string> expected = source.Skip(start ?? 0).ToList();

        List<Page<string>> pages = await pageable.AsPages(start?.ToString(), pageSize).ToListAsync();
        pages.Should().HaveCount((int)Math.Ceiling((15.0 - (start ?? 0)) / (pageSize ?? 3)));
        pages.SelectMany(x => x.Values).Should().BeEquivalentTo(expected);
        counter.Callbacks.Should().Be(pages.Count);
    }

    static List<string> CreateSource(int count)
    {
        return Enumerable.Range(0, count).Select(x => $"item_{x}").ToList();
    }

    static AsyncPageable<string> CreatePageable(List<string> source, CallbackCounter counter)
    {
        Task<Page<string>> Callback(string? continuation, int? pageSize, CancellationToken cancellation)
        {
            counter.Callbacks++;
            int skip = continuation is string c ? int.Parse(c) : 0;
            int take = pageSize ?? 3;
            IEnumerable<string> values = source.Skip(skip).Take(take);
            int total = skip + take;
            string? next = total < source.Count ? total.ToString() : null;
            Page<string> page = new(values.ToList(), next);
            return Task.FromResult(page);
        }

        return Pageable.Create(Callback);
    }

    class CallbackCounter // Mutable box for Callbacks
    {
        public int Callbacks { get; set; }
    }
}
