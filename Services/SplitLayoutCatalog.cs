using System;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Stealth.Services;

public enum SplitMonitorShape
{
    Portrait,
    Standard,
    Ultrawide
}

public sealed class SplitSlot
{
    public double X { get; }
    public double Y { get; }
    public double W { get; }
    public double H { get; }

    public SplitSlot(double x, double y, double w, double h)
    {
        X = x;
        Y = y;
        W = w;
        H = h;
    }
}

public sealed class SplitLayoutDef
{
    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<SplitSlot> Slots { get; }
    public IReadOnlyList<SplitMonitorShape> Shapes { get; }

    public SplitLayoutDef(string id, string name, IReadOnlyList<SplitSlot> slots, params SplitMonitorShape[] shapes)
    {
        Id = id;
        Name = name;
        Slots = slots;
        Shapes = shapes;
    }
}

public static class SplitLayoutCatalog
{
    public const string DefaultId = "half";
    public const double UltrawideAspect = 2.0;
    public const double PortraitAspect = 1.0;

    private const SplitMonitorShape P = SplitMonitorShape.Portrait;
    private const SplitMonitorShape S = SplitMonitorShape.Standard;
    private const SplitMonitorShape U = SplitMonitorShape.Ultrawide;

    public static readonly IReadOnlyList<SplitLayoutDef> All = new List<SplitLayoutDef>
    {
        new("half", "50/50", new[]
        {
            new SplitSlot(0, 0, 0.5, 1), new SplitSlot(0.5, 0, 0.5, 1)
        }, S, U),
        new("wide-left", "65/35", new[]
        {
            new SplitSlot(0, 0, 0.65, 1), new SplitSlot(0.65, 0, 0.35, 1)
        }, S, U),
        new("wide-right", "35/65", new[]
        {
            new SplitSlot(0, 0, 0.35, 1), new SplitSlot(0.35, 0, 0.65, 1)
        }, S, U),
        new("one-two-right", "1 + 2 right", new[]
        {
            new SplitSlot(0, 0, 0.5, 1), new SplitSlot(0.5, 0, 0.5, 0.5), new SplitSlot(0.5, 0.5, 0.5, 0.5)
        }, S, U),
        new("two-one-right", "2 left + 1", new[]
        {
            new SplitSlot(0, 0, 0.5, 0.5), new SplitSlot(0, 0.5, 0.5, 0.5), new SplitSlot(0.5, 0, 0.5, 1)
        }, S, U),
        new("grid-2x2", "2x2", new[]
        {
            new SplitSlot(0, 0, 0.5, 0.5), new SplitSlot(0.5, 0, 0.5, 0.5),
            new SplitSlot(0, 0.5, 0.5, 0.5), new SplitSlot(0.5, 0.5, 0.5, 0.5)
        }, S, U, P),
        new("cols-3", "3 columns", new[]
        {
            new SplitSlot(0, 0, 1.0 / 3.0, 1), new SplitSlot(1.0 / 3.0, 0, 1.0 / 3.0, 1), new SplitSlot(2.0 / 3.0, 0, 1.0 / 3.0, 1)
        }, U),
        new("cols-25-50-25", "25/50/25", new[]
        {
            new SplitSlot(0, 0, 0.25, 1), new SplitSlot(0.25, 0, 0.5, 1), new SplitSlot(0.75, 0, 0.25, 1)
        }, U),
        new("cols-stacked-ends", "Stacked ends", new[]
        {
            new SplitSlot(0, 0, 0.25, 0.5), new SplitSlot(0, 0.5, 0.25, 0.5), new SplitSlot(0.25, 0, 0.5, 1),
            new SplitSlot(0.75, 0, 0.25, 0.5), new SplitSlot(0.75, 0.5, 0.25, 0.5)
        }, U),
        new("rows-half", "Top / bottom", new[]
        {
            new SplitSlot(0, 0, 1, 0.5), new SplitSlot(0, 0.5, 1, 0.5)
        }, S, U, P),
        new("one-two-bottom", "1 top + 2 below", new[]
        {
            new SplitSlot(0, 0, 1, 0.5), new SplitSlot(0, 0.5, 0.5, 0.5), new SplitSlot(0.5, 0.5, 0.5, 0.5)
        }, S, U, P),
        new("two-one-bottom", "2 above + 1 bottom", new[]
        {
            new SplitSlot(0, 0, 0.5, 0.5), new SplitSlot(0.5, 0, 0.5, 0.5), new SplitSlot(0, 0.5, 1, 0.5)
        }, S, U, P),
        new("rows-3", "3 rows", new[]
        {
            new SplitSlot(0, 0, 1, 1.0 / 3.0), new SplitSlot(0, 1.0 / 3.0, 1, 1.0 / 3.0), new SplitSlot(0, 2.0 / 3.0, 1, 1.0 / 3.0)
        }, P)
    };

    public static SplitMonitorShape ShapeOf(double aspect)
    {
        if (double.IsNaN(aspect) || double.IsInfinity(aspect) || aspect <= 0) return SplitMonitorShape.Standard;
        if (aspect < PortraitAspect) return SplitMonitorShape.Portrait;
        if (aspect >= UltrawideAspect) return SplitMonitorShape.Ultrawide;
        return SplitMonitorShape.Standard;
    }

    public static IReadOnlyList<SplitLayoutDef> For(SplitMonitorShape shape)
    {
        return All.Where(l => l.Shapes.Contains(shape)).ToList();
    }

    public static SplitLayoutDef Find(string? id)
    {
        return All.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    }
}