namespace HyprStart.Core.Layout;

/// <summary>Прямоугольник в DIP (device independent pixels), левый верхний угол — X/Y.</summary>
public readonly record struct PixelRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public override string ToString() => $"{X:0.#},{Y:0.#} {Width:0.#}x{Height:0.#}";
}
