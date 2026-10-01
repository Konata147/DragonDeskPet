using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DragonDeskPet.Core;
using Image = System.Windows.Controls.Image;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace DragonDeskPet;

// Small UI-only drawings shared by the chooser and the dragged treat.
internal static class PetChoiceIcons
{
    private static readonly IReadOnlyDictionary<PetSnack, ImageSource> Snacks =
        Enum.GetValues<PetSnack>().ToDictionary(snack => snack, DrawSnack);
    private static readonly IReadOnlyDictionary<PetDance, ImageSource> Dances =
        Enum.GetValues<PetDance>().ToDictionary(dance => dance, DrawDance);

    internal static ImageSource SnackSource(PetSnack snack) => Snacks[snack];

    internal static Image Snack(PetSnack snack, double size) => Icon(SnackSource(snack), size);

    internal static Image Dance(PetDance dance, double size) => Icon(Dances[dance], size);

    private static Image Icon(ImageSource source, double size) => new()
    {
        Source = source, Width = size, Height = size, Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false, SnapsToDevicePixels = true
    };

    private static ImageSource DrawSnack(PetSnack snack)
    {
        var drawing = Canvas();
        switch (snack)
        {
            case PetSnack.Cookie:
                Oval(drawing, 12, 12, 9, 9, "#E4B87C", "#A87545", 1.2);
                Oval(drawing, 8, 8, 1.8, 1.8, "#8F5D43");
                Oval(drawing, 16, 10, 1.5, 1.5, "#8F5D43");
                Oval(drawing, 11, 16, 1.7, 1.7, "#8F5D43");
                break;
            case PetSnack.Strawberry:
                Shape(drawing, "M 12,5 C 16,4 20,7 19,12 C 18,17 14,21 12,22 C 10,21 6,17 5,12 C 4,7 8,4 12,5 Z",
                    "#ED6683", "#B73F60", 1);
                Shape(drawing, "M 7,6 L 5,3 L 10,4 L 12,1.8 L 14,4 L 19,3 L 17,6 L 12,7 Z", "#71A673", "#55875A", .7);
                Oval(drawing, 8.5, 11, .7, 1.1, "#FFE6AC");
                Oval(drawing, 15.5, 11, .7, 1.1, "#FFE6AC");
                Oval(drawing, 12, 16, .7, 1.1, "#FFE6AC");
                break;
            case PetSnack.Cake:
                Shape(drawing, "M 4,10 L 17,5.5 L 20,17 L 5,20 Z", "#F7D8B6", "#AD7C7A", 1);
                Shape(drawing, "M 4.5,13 L 18.2,9 L 18.8,12 L 4.8,16 Z", "#EF9CB8");
                Shape(drawing, "M 4,10 L 17,5.5 L 18,9 L 4.5,13 Z", "#FFF7EE", "#D5A7B7", .6);
                Oval(drawing, 15.2, 4.5, 2, 1.7, "#DC6180");
                break;
            case PetSnack.Candy:
                Shape(drawing, "M 5,9 L 1.5,7 L 2.5,12 L 1.5,17 L 5,15 Z", "#D1ACF0", "#916ABD", .8);
                Shape(drawing, "M 19,9 L 22.5,7 L 21.5,12 L 22.5,17 L 19,15 Z", "#D1ACF0", "#916ABD", .8);
                Shape(drawing, "M 6,8 C 9,6 15,6 18,8 L 19,15 C 16,18 8,18 5,15 Z", "#B791E1", "#7655AA", 1);
                Shape(drawing, "M 9,8 L 13,17", null, "#F7E9FF", 1.2);
                Shape(drawing, "M 14,7.5 L 18,15", null, "#F7E9FF", 1.2);
                break;
            case PetSnack.CottonCandy:
                Shape(drawing, "M 12,15 L 12,22", null, "#A77957", 1.5);
                Oval(drawing, 8.5, 11, 4.2, 4.2, "#F8C7E3", "#D998C4", .7);
                Oval(drawing, 14.7, 10.5, 4.3, 4.3, "#F8C7E3", "#D998C4", .7);
                Oval(drawing, 12, 7.7, 4.8, 4.8, "#FFD8EB", "#D998C4", .7);
                Oval(drawing, 11.8, 13, 5.8, 4.4, "#FFD8EB");
                break;
        }
        return Finish(drawing);
    }

    private static ImageSource DrawDance(PetDance dance)
    {
        var drawing = Canvas();
        switch (dance)
        {
            case PetDance.Step:
                Oval(drawing, 7, 9, 2.7, 4, "#9972CD");
                Oval(drawing, 16, 15, 2.7, 4, "#7653AD");
                Oval(drawing, 10.3, 4.4, 1, 1.2, "#9972CD");
                Oval(drawing, 19.3, 10.4, 1, 1.2, "#7653AD");
                break;
            case PetDance.Guofeng:
                Shape(drawing, "M 4,17 C 8,4 12,18 17,6 C 20,9 18,17 13,20 C 10,17 7,20 4,17 Z",
                    "#D7B8ED", "#8C65BD", 1);
                Shape(drawing, "M 7,15 C 11,9 14,16 18,8", null, "#FFF9FF", 1.2);
                break;
            case PetDance.WingTail:
                Shape(drawing, "M 3,16 C 7,4 13,5 16,9 C 12,9 11,12 10,15 C 7,13 5,15 3,16 Z",
                    "#D5C0F0", "#7957AA", 1);
                Shape(drawing, "M 10,15 C 15,13 19,15 20,19 C 18,20 16,20 14,19", null, "#7957AA", 1.4);
                Shape(drawing, "M 6,13 C 9,10 11,10 14,9", null, "#9A78C8", .8);
                break;
        }
        return Finish(drawing);
    }

    private static DrawingGroup Canvas()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null,
            new RectangleGeometry(new Rect(0, 0, 24, 24))));
        return group;
    }

    private static void Shape(DrawingGroup group, string data, string? fill,
        string? stroke = null, double thickness = 0) =>
        group.Children.Add(new GeometryDrawing(Paint(fill), Ink(stroke, thickness), Geometry.Parse(data)));

    private static void Oval(DrawingGroup group, double x, double y, double rx, double ry,
        string fill, string? stroke = null, double thickness = 0) =>
        group.Children.Add(new GeometryDrawing(Paint(fill), Ink(stroke, thickness),
            new EllipseGeometry(new Point(x, y), rx, ry)));

    private static Brush? Paint(string? color) => color is null ? null :
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);

    private static Pen? Ink(string? color, double thickness) => color is null ? null :
        new Pen(Paint(color), thickness);

    private static ImageSource Finish(DrawingGroup drawing)
    {
        drawing.Freeze();
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
}
