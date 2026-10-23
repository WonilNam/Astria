namespace Astria.Desktop.Services;

public readonly record struct OrbitCard(double X, double Y, double Depth, double Scale, double Opacity, double Rotation);

/// <summary>Time-based, UI-independent orbit camera with damped angular momentum.</summary>
public sealed class OrbitMotion
{
    private double _yawVelocity;
    private double _pitchVelocity;
    public double Yaw { get; private set; }
    public double Pitch { get; private set; } = -.16;
    public double Roll { get; private set; }
    public double Zoom { get; private set; } = 1;
    public bool IsMoving => Math.Abs(_yawVelocity) + Math.Abs(_pitchVelocity) > .008;

    public void Stop() { _yawVelocity = 0; _pitchVelocity = 0; }
    public void Focus(int index, int count)
    {
        Stop();
        Yaw = count > 0 ? -index * Math.Tau / count : 0;
    }

    public void Drag(double dx, double dy, double seconds, bool roll)
    {
        var dt = Math.Clamp(seconds, .008, .08);
        if (roll) { Roll += dx * .005; Stop(); return; }
        var yaw = dx * .006;
        var pitch = dy * .006;
        Yaw += yaw;
        Pitch = Math.Clamp(Pitch + pitch, -1.35, 1.35);
        _yawVelocity = Math.Clamp(yaw / dt, -5, 5);
        _pitchVelocity = Math.Clamp(pitch / dt, -3, 3);
    }

    public void Advance(double seconds)
    {
        var dt = Math.Clamp(seconds, 0, .05);
        Yaw += _yawVelocity * dt;
        Pitch = Math.Clamp(Pitch + _pitchVelocity * dt, -1.35, 1.35);
        var damping = Math.Exp(-4.2 * dt);
        _yawVelocity *= damping; _pitchVelocity *= damping;
        if (!IsMoving) Stop();
    }

    public void ChangeZoom(double delta) => Zoom = Math.Clamp(Zoom * Math.Exp(delta * .08), .6, 1.45);
    public void Revolve(double seconds) => Yaw += Math.Clamp(seconds, 0, .05) * .045;

    public OrbitCard Project(int index, int count, double radius)
    {
        if (count < 1) return default;
        var angle = count == 1 ? 0 : index * Math.Tau / count + Yaw;
        var x = Math.Sin(angle) * radius;
        var z = Math.Cos(angle) * radius;
        var y = Math.Sin(angle * 2) * radius * .12;
        var vertical = y * Math.Cos(Pitch) - z * Math.Sin(Pitch);
        var depth = y * Math.Sin(Pitch) + z * Math.Cos(Pitch);
        var px = x * Math.Cos(Roll) - vertical * Math.Sin(Roll);
        var py = x * Math.Sin(Roll) + vertical * Math.Cos(Roll);
        var perspective = 900 / (900 - depth * Zoom);
        return new(px * Zoom * perspective, py * Zoom * perspective, depth,
            (.65 + (depth / Math.Max(1, radius) + 1) * .175) * Zoom,
            .35 + (depth / Math.Max(1, radius) + 1) * .325, Roll * 180 / Math.PI + Math.Sin(angle) * 7);
    }

    public OrbitCard ProjectSpace(int index, int count, double phase)
    {
        var angle = count <= 1 ? 0 : index * Math.Tau / count + Yaw;
        var point = RingPoint(angle);
        return point with { Y = point.Y + Math.Sin(phase + index * 1.4) * 2,
            Rotation = Roll * 180 / Math.PI + Math.Sin(angle) * 5 };
    }

    public OrbitCard RingPoint(double angle)
    {
        var x = Math.Sin(angle) * 350;
        var y = Math.Cos(angle) * 145;
        var z = Math.Cos(angle) * 175;
        var ry = y * Math.Cos(Pitch) - z * Math.Sin(Pitch);
        var depth = z * Math.Cos(Pitch) + y * Math.Sin(Pitch);
        var perspective = 1100 / (1100 - depth * Zoom);
        var px = x * Math.Cos(Roll) - ry * Math.Sin(Roll);
        var py = x * Math.Sin(Roll) + ry * Math.Cos(Roll);
        return new(px * perspective * Zoom, py * perspective * Zoom, depth,
            Math.Clamp(perspective * .84 * Zoom, .48, 1.15), Math.Clamp(.8 + depth / 650, .52, 1), 0);
    }
}
