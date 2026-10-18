using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;

namespace IntentLens.Player;

internal static class NativeArrow
{
    internal static Sprite2D Create(Control parent, Vector2[] points, float width = 20)
    {
        var texture = PreloadManager.Cache.GetTexture2D("res://images/ui/cards/upgrade_preview/upgrade_arrow.png");
        var to = points[^1];
        var direction = (to - points[^2]).Normalized();
        var tail = points.ToArray();
        tail[^1] = to - direction * Math.Min(width * .65f, (to - points[^2]).Length() * .6f);
        var line = new Line2D
        {
            Name = "IntentLensArrowTail", Points = tail, Width = 1.5f,
            DefaultColor = StsColors.gold, Antialiased = true
        };
        parent.AddChild(line);
        var head = new Sprite2D
        {
            Name = "IntentLensNativeArrow", Texture = texture, Position = to - direction * width / 2,
            Rotation = direction.Angle(), Scale = Vector2.One * (width / texture.GetWidth())
        };
        parent.AddChild(head);
        return head;
    }
}
