using Autodesk.Revit.DB;

namespace RevitTrueGltf
{
    internal class UnitUtils
    {
        public static double Feet2Meter(double value)
        {
            return Autodesk.Revit.DB.UnitUtils.Convert(value, DisplayUnitType.DUT_DECIMAL_FEET, DisplayUnitType.DUT_METERS);
        }
    }
}
