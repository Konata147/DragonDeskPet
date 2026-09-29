namespace DragonDeskPet.Core;

public static class PetTreatDrop
{
    public static bool IsAccepted(bool active, bool dragging, bool eligible, double distance, double scale) =>
        active && dragging && eligible && scale > 0 && double.IsFinite(distance)
        && distance <= 24 * scale;
}
