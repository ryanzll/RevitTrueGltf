namespace RevitTrueGltf
{
    internal class UnitUtils
    {
        public static double Feet2Meter(double value)
        {
#if REVIT2020 || REVIT2021
            return Autodesk.Revit.DB.UnitUtils.Convert(value, Autodesk.Revit.DB.DisplayUnitType.DUT_DECIMAL_FEET, Autodesk.Revit.DB.DisplayUnitType.DUT_METERS);
#else
            return Autodesk.Revit.DB.UnitUtils.Convert(value, Autodesk.Revit.DB.UnitTypeId.Feet, Autodesk.Revit.DB.UnitTypeId.Meters);
#endif
        }
    }
}
