using Silk.NET.OpenGL;

namespace Technolize.Rendering.Graphics;

/// <summary>
/// A GPU timer using an OpenGL <c>GL_TIME_ELAPSED</c> query (ARB_timer_query, core since 3.3). It
/// measures the wall-clock time the GPU spends executing the commands issued between <see cref="Begin"/>
/// and <see cref="End"/>, which \u2014 unlike a CPU stopwatch around the draw \u2014 excludes driver queueing and
/// the synchronous pixel readback, isolating real shader execution cost.
///
/// One query object is reused; <see cref="Begin"/>/<see cref="End"/> must be paired and not nested.
/// </summary>
public sealed class GlTimerQuery : IDisposable
{
    private readonly GL _gl;
    private readonly uint _query;

    public GlTimerQuery(GL gl)
    {
        _gl = gl;
        _query = gl.GenQuery();
    }

    /// <summary>Starts timing the GPU commands that follow.</summary>
    public void Begin() => _gl.BeginQuery(QueryTarget.TimeElapsed, _query);

    /// <summary>Stops timing.</summary>
    public void End() => _gl.EndQuery(QueryTarget.TimeElapsed);

    /// <summary>
    /// Blocks until the timed range's result is available and returns the elapsed GPU time in
    /// milliseconds. Call once after a matched <see cref="Begin"/>/<see cref="End"/>.
    /// </summary>
    public double ResolveElapsedMs()
    {
        _gl.GetQueryObject(_query, QueryObjectParameterName.QueryResult, out ulong nanoseconds);
        return nanoseconds / 1_000_000.0;
    }

    public void Dispose() => _gl.DeleteQuery(_query);
}
