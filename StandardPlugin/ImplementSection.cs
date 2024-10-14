using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AgGateway.ADAPT.ApplicationDataModel.Equipment;
using AgGateway.ADAPT.ApplicationDataModel.LoggedData;
using AgGateway.ADAPT.ApplicationDataModel.Representations;
using NetTopologySuite.Geometries;

namespace AgGateway.ADAPT.StandardPlugin
{
    internal class SectionDefinition
    {
        public SectionDefinition(DeviceElementUse deviceElementUse, DeviceElementConfiguration deviceElementConfiguration, DeviceElement deviceElement, List<TypeMapping> typeMappings)
        {
            DeviceElement = deviceElement;
            
            WorkstateDefinition = deviceElementUse.GetWorkingDatas().OfType<EnumeratedWorkingData>().FirstOrDefault(x => x.Representation.Code == "dtRecordingStatus");
            FactoredDefinitionsBySourceCode = new Dictionary<string, FactoredWorkingData>();

            //Add only the variables we can map to a standard type
            var numericWorkingDatas = deviceElementUse.GetWorkingDatas().OfType<NumericWorkingData>().Where(nwd => typeMappings.Any(m => m.Source == nwd.Representation.Code));
            WidthM = deviceElementConfiguration.WidthM();
            var definitions = numericWorkingDatas.Select(nwd => new FactoredWorkingData(nwd, WidthM, WidthM, typeMappings.First(m => m.Source == nwd.Representation.Code)));
            FactoredDefinitionsBySourceCode = definitions.ToDictionary(d => d.WorkingData.Representation.Code);
            Offset = deviceElementConfiguration.AsOffset();
        }
        public Offset Offset { get; set; }
        public DeviceElement DeviceElement { get; set; }
        public EnumeratedWorkingData WorkstateDefinition { get; set; }
        public Dictionary<string, FactoredWorkingData> FactoredDefinitionsBySourceCode { get; set; }

        public double WidthM { get; set; }

        public string GetDefinitionKey()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(DeviceElement?.Description ?? string.Empty);
            builder.Append(Offset.X?.ToString() ?? string.Empty);
            builder.Append(Offset.Y?.ToString() ?? string.Empty);
            builder.Append(WidthM.ToString());
            foreach(var factoredDefinition in FactoredDefinitionsBySourceCode.Values)
            {
                builder.Append(factoredDefinition.WorkingData.Representation.Code);
            }
            return builder.ToString().AsMD5Hash();
        }

        public void AddAncestorWorkingDatas(DeviceElementUse ancestorUse, DeviceElementConfiguration ancestorConfig, List<TypeMapping> typeMappings)
        {
            foreach (var workingData in ancestorUse.GetWorkingDatas())
            {
                if (WorkstateDefinition == null &&
                    workingData.Representation.Code == "dtRecordingStatus" &&
                    workingData is EnumeratedWorkingData ewd)
                {
                    WorkstateDefinition = ewd;
                }
                else if (workingData is NumericWorkingData nwd &&
                    typeMappings.Any(m => m.Source == nwd.Representation.Code) &&
                    !FactoredDefinitionsBySourceCode.ContainsKey(nwd.Representation.Code))
                {
                    FactoredDefinitionsBySourceCode.Add(nwd.Representation.Code, new FactoredWorkingData(nwd, ancestorConfig.WidthM(), WidthM, typeMappings.First(m => m.Source == nwd.Representation.Code)));
                }
            }
        }

        public bool IsEngaged(SpatialRecord record)
        {
            if (WorkstateDefinition != null)
            {
                EnumeratedValue engagedValue = record.GetMeterValue(WorkstateDefinition) as EnumeratedValue;
                return engagedValue != null && (engagedValue.Value.Value == "dtiRecordingStatusOn" || engagedValue.Value.Value == "On");
            }
            return true;
        }

        private LeadingEdge _latestLeadingEdge;
        public bool TryGetCoveragePolygon(SpatialRecord record, SpatialRecord previousRecord, out Polygon polygon)
        {
            polygon = null;
            var adaptPoint = (ApplicationDataModel.Shapes.Point)record.Geometry;
            Point point = new Point(adaptPoint.X, adaptPoint.Y);

            if (point.X == 0 && point.Y == 0)
            {
                return false;
            }

            Point priorPoint = null;
            if (previousRecord != null)
            {
                var priorADAPTPoint = (ApplicationDataModel.Shapes.Point)previousRecord.Geometry;
                priorPoint = new Point(priorADAPTPoint.X, priorADAPTPoint.Y);
            }

            double bearing = 0;
            if (FactoredDefinitionsBySourceCode.ContainsKey("vrHeading"))
            {
                bearing = ((NumericRepresentationValue)record.GetMeterValue(FactoredDefinitionsBySourceCode["vrHeading"].WorkingData)).Value.Value;
            }
            else if (priorPoint != null)
            {
                bearing = GeometryExporter.HaversineBearing(priorPoint, point);
            }

            var x = point.Destination(Offset.X ?? 0d, bearing % 360d);
            var xy = x.Destination(Offset.Y ?? 0d, bearing + 90d % 360d);
            double? reportedDistance = null;
            if (FactoredDefinitionsBySourceCode.ContainsKey("vrDistanceTraveled"))
            {
                reportedDistance = ((NumericRepresentationValue)record.GetMeterValue(FactoredDefinitionsBySourceCode["vrDistanceTraveled"].WorkingData)).Value.Value;
            }
            polygon = xy.AsCoveragePolygon(WidthM, ref _latestLeadingEdge, bearing, reportedDistance);
            if (polygon.IsEmpty || !polygon.IsValid)
            {
                return false;
            }
            return true;
        }

        public void ClearLeadingEdge()
        {
            _latestLeadingEdge = null;
        }
    }

    internal class LeadingEdge
    {
        public LeadingEdge(Point leftPoint, Point centerPoint, Point rightPoint)
        {
            Left = leftPoint;
            Center = centerPoint;
            Right = rightPoint;
        }
        public LeadingEdge(Point leadingPoint, double bearing, double width)
        {
            Left = leadingPoint.Destination(width / 2d, bearing - 90d % 360d);
            Center = leadingPoint;
            Right = leadingPoint.Destination(width / 2d, bearing + 90d % 360d);
        }

        public Point Left { get; set; }
        public Point Center { get; set; }
        public Point Right { get; set; }    

        public double GetBearing(Point nextPoint)
        {
            return GeometryExporter.HaversineBearing(Center, nextPoint);
        }
        public double GetDistance(Point nextPoint)
        {
            return GeometryExporter.HaversineDistance(Center, nextPoint);
        }
    }

    internal class Offset
    {
        public Offset(double? x, double? y, double? z)
        {
            X = x;
            Y = y;
            Z = z;
        }
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Z { get; set; }

        public void Add(Offset other)
        {
            if (!X.HasValue) X = 0;
            X += other.X ?? 0;
            if (!Y.HasValue) Y = 0;
            Y += other.Y ?? 0;
            if (!Z.HasValue) Z = 0;
            Z += other.Z ?? 0;
        }
    }

    /// <summary>
    /// In cases where an implement reports a single total value, e.g., at the implement level and then models multiple sections for on/off values only
    /// The total must be factored down for the width of the section relative to the total width of the implement
    /// For rates, percentages, etc.  No factor is needed.
    /// </summary>
    internal class FactoredWorkingData
    {
        public FactoredWorkingData(NumericWorkingData workingData, double dataWidthM, double sectionWidthM, TypeMapping mapping)
        {
            WorkingData = workingData;
            
            if (mapping.ShouldFactor && 
                dataWidthM > 0d &&
                Math.Abs(dataWidthM - sectionWidthM) > .001) //Floating point values are sometimes not exact
            {
                Factor = sectionWidthM / dataWidthM;
            }
            else
            {
                Factor = 1d;
            }
        }
        public double Factor { get; set; }
        public NumericWorkingData WorkingData { get; set; }
    }
}