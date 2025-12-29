namespace JitHub.Dashboard.Layouts;

public readonly record struct LayoutSize(double Width, double Height);

public readonly record struct LayoutPoint(double X, double Y);

public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public static LayoutRect FromSize(LayoutPoint origin, LayoutSize size) => new(origin.X, origin.Y, size.Width, size.Height);

    public bool IsFinite =>
        double.IsFinite(X) &&
        double.IsFinite(Y) &&
        double.IsFinite(Width) &&
        double.IsFinite(Height);
}

public readonly record struct LayoutTransform(double TranslateX, double TranslateY, double RotationDegrees, double Scale)
{
    public bool IsFinite =>
        double.IsFinite(TranslateX) &&
        double.IsFinite(TranslateY) &&
        double.IsFinite(RotationDegrees) &&
        double.IsFinite(Scale);
}

public sealed record CardDeckLayoutItem(LayoutRect Rect, LayoutTransform Transform);
