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
        public SectionDefinition(DeviceElementUse deviceElementUse, DeviceElementConfiguration deviceElementConfiguration, DeviceElement deviceElement, Dictionary<string, string> typeMappings)
        {
            DeviceElement = deviceElement;
            
            WorkstateDefinition = deviceElementUse.GetWorkingDatas().OfType<EnumeratedWorkingData>().FirstOrDefault(x => x.Representation.Code == "dtRecordingStatus");
            NumericDefinitions = new List<NumericWorkingData>();

            //Add only the variables we can map to a standard type
            NumericDefinitions.AddRange(deviceElementUse.GetWorkingDatas().OfType<NumericWorkingData>().Where(nwd => typeMappings.ContainsKey(nwd.Representation.Code)));

            if (deviceElementConfiguration is SectionConfiguration sectionConfiguration)
            {
                WidthM = sectionConfiguration.SectionWidth?.AsConvertedDouble("m") ?? 0d;
            }
            else if (deviceElementConfiguration is ImplementConfiguration implementConfiguration)
            {
                WidthM = implementConfiguration.PhysicalWidth.AsConvertedDouble("m") ?? implementConfiguration.Width?.AsConvertedDouble("m") ?? 0d;
            }

            Offset = deviceElementConfiguration.AsOffset();
        }
        public Offset Offset { get; set; }
        public DeviceElement DeviceElement { get; set; }
        public EnumeratedWorkingData WorkstateDefinition { get; set; }
        public List<NumericWorkingData> NumericDefinitions { get; set; }

        public double WidthM { get; set; }

        public string GetDefinitionKey()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(DeviceElement?.Description ?? string.Empty);
            builder.Append(Offset.X?.ToString() ?? string.Empty);
            builder.Append(Offset.Y?.ToString() ?? string.Empty);
            builder.Append(WidthM.ToString());
            foreach(var workingData in NumericDefinitions)
            {
                builder.Append(workingData.Representation.Code);
            }
            return builder.ToString().AsMD5Hash();
        }

        public void AddAncestorWorkingDatas(DeviceElementUse ancestorUse, Dictionary<string, string> typeMappings)
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
                    typeMappings.ContainsKey(nwd.Representation.Code) &&
                    !NumericDefinitions.Any(x => x.Representation.Code == nwd.Representation.Code))
                {
                    NumericDefinitions.Add(nwd);
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
            var headingData = NumericDefinitions.FirstOrDefault(d => d.Representation.Code == "vrHeading");
            if (headingData != null)
            {
                bearing = ((NumericRepresentationValue)record.GetMeterValue(headingData)).Value.Value;
            }
            else if (priorPoint != null)
            {
                bearing = GeometryExporter.HaversineBearing(priorPoint, point);
            }

            var x = point.Destination(Offset.X ?? 0d, bearing % 360d);
            var xy = x.Destination(Offset.Y ?? 0d, bearing + 90d % 360d);
            double? reportedDistance = null;
            var distanceData = NumericDefinitions.FirstOrDefault(d => d.Representation.Code == "vrDistanceTraveled");
            if (distanceData != null)
            {
                reportedDistance = ((NumericRepresentationValue)record.GetMeterValue(distanceData)).Value.Value;
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
}