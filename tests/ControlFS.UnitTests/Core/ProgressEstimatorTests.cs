using ControlFS.Core.Models;
using ControlFS.Core.Text;

namespace ControlFS.UnitTests.Core;

/// <summary>Regras da estimativa de tempo restante (#257): só quando confiável, sempre rotulada, sem recuar.</summary>
public class ProgressEstimatorTests
{
    private static OperationProgress Bytes(long done, long total, int items = 0, int? itemsTotal = 1) => new("x", items, itemsTotal, done, total);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Estimate_waits_for_three_seconds_and_five_percent_then_tracks_the_speed()
    {
        var estimator = new ProgressEstimator(S(0));
        const long total = 1_000_000_000;
        // 10 MB/s: 1% em 1 s (pouco tempo, pouco andamento), 2,5% em 2,5 s: ainda sem estimativa.
        Assert.Null(estimator.Update(S(1), Bytes(10_000_000, total)).Remaining);
        Assert.Null(estimator.Update(S(2.5), Bytes(25_000_000, total)).Remaining);
        // 3 s, mas só 3%: sem estimativa (menos de 5%).
        Assert.Null(estimator.Update(S(3), Bytes(30_000_000, total)).Remaining);
        // 6 s, 6%: as duas condições valem; ~94 MB/s... aqui 10 MB/s: faltam ~94 s.
        var snapshot = estimator.Update(S(6), Bytes(60_000_000, total));
        Assert.NotNull(snapshot.Remaining);
        Assert.InRange(snapshot.Remaining!.Value.TotalSeconds, 80, 110);
        Assert.InRange(snapshot.BytesPerSecond!.Value, 8_000_000, 12_000_000);
    }

    [Fact]
    public void Unknown_total_is_indeterminate_without_percent_or_estimate()
    {
        var estimator = new ProgressEstimator(S(0));
        var snapshot = new ProgressSnapshot(null, null, null);
        for (var t = 1; t <= 20; t++) snapshot = estimator.Update(S(t), new OperationProgress("lendo…", t, null, t * 1_000_000L, null));
        Assert.Null(snapshot.Fraction);
        Assert.Null(snapshot.Remaining);

        var flagged = estimator.Update(S(21), new OperationProgress("x", 5, 10, 500, 1000) { Indeterminate = true });
        Assert.Null(flagged.Fraction);
        Assert.Null(flagged.Remaining);
    }

    [Fact]
    public void Fraction_never_goes_backwards_when_the_total_grows()
    {
        var estimator = new ProgressEstimator(S(0));
        var first = estimator.Update(S(1), Bytes(500, 1000)).Fraction;
        var after = estimator.Update(S(2), Bytes(600, 4000)).Fraction; // o total cresceu: bruto 15%
        Assert.Equal(0.5, first);
        Assert.Equal(0.5, after);
    }

    [Fact]
    public void Time_spent_paused_does_not_count_and_does_not_slow_the_estimate()
    {
        var estimator = new ProgressEstimator(S(0));
        const long total = 100_000_000;
        estimator.Update(S(2), Bytes(20_000_000, total)); // 10 MB/s
        estimator.Pause(S(2));
        Assert.Null(estimator.Update(S(600), Bytes(20_000_000, total)).Remaining);
        estimator.Resume(S(602)); // 10 min parado
        var snapshot = estimator.Update(S(604), Bytes(40_000_000, total));
        Assert.NotNull(snapshot.Remaining);
        Assert.InRange(snapshot.Remaining!.Value.TotalSeconds, 4, 10); // ~10 MB/s: faltam ~6 s, não minutos
    }

    [Fact]
    public void Item_only_work_estimates_by_items_and_estimates_are_always_labelled()
    {
        var estimator = new ProgressEstimator(S(0));
        ProgressSnapshot snapshot = new(null, null, null);
        for (var t = 1; t <= 10; t++) snapshot = estimator.Update(S(t), new OperationProgress("a", t * 10, 1000, 0, null));
        Assert.Equal(0.1, snapshot.Fraction);
        Assert.NotNull(snapshot.Remaining);
        Assert.EndsWith("restantes (estimativa)", ProgressText.Remaining(snapshot.Remaining!.Value), StringComparison.Ordinal);
        Assert.Equal("menos de 10 s restantes (estimativa)", ProgressText.Remaining(S(4)));
        Assert.Equal("cerca de 2 min restantes (estimativa)", ProgressText.Remaining(S(118)));
    }

    [Fact]
    public void Summary_shows_percent_items_and_bytes_but_no_percent_without_a_total()
    {
        var known = new OperationProgress("f", 3, 10, 12 << 20, 80 << 20);
        var text = ProgressText.Summary(known, new ProgressSnapshot(0.15, 25 << 20, S(120)));
        Assert.Equal("15% · 3 de 10 itens · 12 MB de 80 MB · 25 MB/s · cerca de 2 min restantes (estimativa)", text);

        var unknown = ProgressText.Summary(new OperationProgress("f", 3, null, 12 << 20, null), new ProgressSnapshot(null, null, null));
        Assert.DoesNotContain("%", unknown, StringComparison.Ordinal);
        Assert.DoesNotContain("estimativa", unknown, StringComparison.Ordinal);
        Assert.StartsWith("sem total conhecido", unknown, StringComparison.Ordinal);
    }
}
