using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;

namespace SsisAiRuntime.MutationHost
{
    public sealed class DesignerNodePosition : IEquatable<DesignerNodePosition>
    {
        public DesignerNodePosition(decimal left, decimal top, decimal width, decimal height)
        {
            if (width <= 0 || height <= 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public decimal Left { get; }
        public decimal Top { get; }
        public decimal Width { get; }
        public decimal Height { get; }
        public decimal Right => checked(Left + Width);
        public decimal Bottom => checked(Top + Height);

        public bool Equals(DesignerNodePosition other) => other != null && Left == other.Left && Top == other.Top &&
            Width == other.Width && Height == other.Height;
        public override bool Equals(object obj) => Equals(obj as DesignerNodePosition);
        public override int GetHashCode() => (((Left.GetHashCode() * 397) ^ Top.GetHashCode()) * 397 ^
            Width.GetHashCode()) * 397 ^ Height.GetHashCode();
    }

    public sealed class DesignerPoint : IEquatable<DesignerPoint>
    {
        public DesignerPoint(decimal x, decimal y) { X = x; Y = y; }
        public decimal X { get; }
        public decimal Y { get; }
        public bool Equals(DesignerPoint other) => other != null && X == other.X && Y == other.Y;
        public override bool Equals(object obj) => Equals(obj as DesignerPoint);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
    }

    public sealed class DesignerBounds : IEquatable<DesignerBounds>
    {
        public DesignerBounds(decimal left, decimal top, decimal width, decimal height)
        {
            if (width < 0 || height < 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public decimal Left { get; }
        public decimal Top { get; }
        public decimal Width { get; }
        public decimal Height { get; }
        public decimal Right => checked(Left + Width);
        public decimal Bottom => checked(Top + Height);
        public bool Equals(DesignerBounds other) => other != null && Left == other.Left && Top == other.Top &&
            Width == other.Width && Height == other.Height;
        public override bool Equals(object obj) => Equals(obj as DesignerBounds);
        public override int GetHashCode() => (((Left.GetHashCode() * 397) ^ Top.GetHashCode()) * 397 ^
            Width.GetHashCode()) * 397 ^ Height.GetHashCode();
    }

    public sealed class DesignerNode
    {
        public DesignerNode(string ownerNativeId, string nativeId, string layoutId, DesignerNodePosition position)
        {
            if (string.IsNullOrWhiteSpace(ownerNativeId)) { throw new ArgumentException("An owner native ID is required.", nameof(ownerNativeId)); }
            if (string.IsNullOrWhiteSpace(nativeId)) { throw new ArgumentException("A native ID is required.", nameof(nativeId)); }
            OwnerNativeId = ownerNativeId;
            NativeId = nativeId;
            LayoutId = layoutId ?? string.Empty;
            Position = position;
        }

        public string OwnerNativeId { get; }
        public string NativeId { get; }
        public string LayoutId { get; }
        public DesignerNodePosition Position { get; }
        public bool HasPosition => Position != null;
        public string Key => OwnerNativeId + "\u001f" + (string.IsNullOrEmpty(LayoutId) ? NativeId : LayoutId);
    }

    public sealed class DesignerConnection
    {
        public DesignerConnection(string ownerNativeId, string nativeId, string sourceNativeId,
            string sourcePortNativeId, string destinationNativeId, string destinationPortNativeId,
            IEnumerable<DesignerPoint> routePoints = null, string layoutId = null,
            string sourceLayoutId = null, string sourcePortLayoutId = null,
            string destinationLayoutId = null, string destinationPortLayoutId = null)
        {
            OwnerNativeId = ownerNativeId ?? throw new ArgumentNullException(nameof(ownerNativeId));
            NativeId = nativeId ?? throw new ArgumentNullException(nameof(nativeId));
            SourceNativeId = sourceNativeId ?? throw new ArgumentNullException(nameof(sourceNativeId));
            SourcePortNativeId = sourcePortNativeId ?? throw new ArgumentNullException(nameof(sourcePortNativeId));
            DestinationNativeId = destinationNativeId ?? throw new ArgumentNullException(nameof(destinationNativeId));
            DestinationPortNativeId = destinationPortNativeId ?? throw new ArgumentNullException(nameof(destinationPortNativeId));
            RoutePoints = new ReadOnlyCollection<DesignerPoint>((routePoints ?? Enumerable.Empty<DesignerPoint>()).ToArray());
            LayoutId = layoutId ?? string.Empty;
            SourceLayoutId = sourceLayoutId ?? string.Empty;
            SourcePortLayoutId = sourcePortLayoutId ?? string.Empty;
            DestinationLayoutId = destinationLayoutId ?? string.Empty;
            DestinationPortLayoutId = destinationPortLayoutId ?? string.Empty;
        }

        public string OwnerNativeId { get; }
        public string NativeId { get; }
        public string SourceNativeId { get; }
        public string SourcePortNativeId { get; }
        public string DestinationNativeId { get; }
        public string DestinationPortNativeId { get; }
        public IReadOnlyList<DesignerPoint> RoutePoints { get; }
        public string LayoutId { get; }
        public string SourceLayoutId { get; }
        public string SourcePortLayoutId { get; }
        public string DestinationLayoutId { get; }
        public string DestinationPortLayoutId { get; }
        public string Key => OwnerNativeId + "\u001f" + (string.IsNullOrEmpty(LayoutId) ? NativeId : LayoutId);
    }

    public sealed class DesignerLayoutSnapshot
    {
        private readonly int canvasMargin;

        public DesignerLayoutSnapshot(IEnumerable<DesignerNode> nodes, IEnumerable<DesignerConnection> connections,
            IEnumerable<string> diagnostics = null, int canvasMargin = 32)
        {
            if (canvasMargin < 0) { throw new ArgumentOutOfRangeException(nameof(canvasMargin)); }
            this.canvasMargin = canvasMargin;
            Nodes = new ReadOnlyCollection<DesignerNode>((nodes ?? throw new ArgumentNullException(nameof(nodes))).ToArray());
            Connections = new ReadOnlyCollection<DesignerConnection>((connections ?? throw new ArgumentNullException(nameof(connections))).ToArray());
            Diagnostics = new ReadOnlyCollection<string>((diagnostics ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToArray());
            CanvasBounds = ComputeBounds(Nodes, Connections, canvasMargin);
        }

        public IReadOnlyList<DesignerNode> Nodes { get; }
        public IReadOnlyList<DesignerConnection> Connections { get; }
        public IReadOnlyList<string> Diagnostics { get; }
        public DesignerBounds CanvasBounds { get; }
        public DesignerBounds GetCanvasBounds(string ownerNativeId) => ComputeBounds(
            Nodes.Where(node => node.OwnerNativeId == ownerNativeId),
            Connections.Where(connection => connection.OwnerNativeId == ownerNativeId), canvasMargin);

        private static DesignerBounds ComputeBounds(IEnumerable<DesignerNode> nodes,
            IEnumerable<DesignerConnection> connections, int margin)
        {
            var positioned = nodes.Where(node => node.HasPosition).Select(node => node.Position).ToArray();
            var routePoints = connections.SelectMany(connection => connection.RoutePoints).ToArray();
            if (positioned.Length == 0 && routePoints.Length == 0) { return new DesignerBounds(0, 0, 0, 0); }
            var left = Math.Min(positioned.Length == 0 ? int.MaxValue : positioned.Min(position => position.Left),
                routePoints.Length == 0 ? int.MaxValue : routePoints.Min(point => point.X));
            var top = Math.Min(positioned.Length == 0 ? int.MaxValue : positioned.Min(position => position.Top),
                routePoints.Length == 0 ? int.MaxValue : routePoints.Min(point => point.Y));
            var right = Math.Max(positioned.Length == 0 ? int.MinValue : positioned.Max(position => position.Right),
                routePoints.Length == 0 ? int.MinValue : routePoints.Max(point => point.X));
            var bottom = Math.Max(positioned.Length == 0 ? int.MinValue : positioned.Max(position => position.Bottom),
                routePoints.Length == 0 ? int.MinValue : routePoints.Max(point => point.Y));
            return new DesignerBounds(checked(left - margin), checked(top - margin),
                checked(right - left + margin * 2), checked(bottom - top + margin * 2));
        }
    }

    public sealed class DesignerPlacement
    {
        public DesignerPlacement(string ownerNativeId, decimal left, decimal top, decimal width, decimal height)
        {
            if (width <= 0 || height <= 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
            OwnerNativeId = ownerNativeId ?? throw new ArgumentNullException(nameof(ownerNativeId));
            Position = new DesignerNodePosition(left, top, width, height);
        }
        public string OwnerNativeId { get; }
        public DesignerNodePosition Position { get; }
    }

    public sealed class DesignerLayoutComparison
    {
        internal DesignerLayoutComparison(IEnumerable<string> differences)
        {
            Differences = new ReadOnlyCollection<string>(differences.Distinct(StringComparer.Ordinal)
                .OrderBy(code => code, StringComparer.Ordinal).ToArray());
        }
        public IReadOnlyList<string> Differences { get; }
        public bool Matches => Differences.Count == 0;
    }

    public sealed class DesignerLayoutExtractor
    {
        private const int MaxLayoutCharacters = 1024 * 1024;
        private const long MaxSerializedPackageCharacters = 64L * 1024 * 1024;

        public DesignerLayoutSnapshot Extract(Package package)
        {
            return Extract(package, null);
        }

        public DesignerLayoutSnapshot Extract(Package package, string packagePath)
        {
            if (package == null) { throw new ArgumentNullException(nameof(package)); }
            var nodes = new List<DesignerNode>();
            var connections = new List<DesignerConnection>();
            var diagnostics = new List<string>();
            var containers = Containers(package).ToArray();
            var containerLayoutIds = ContainerLayoutIds(package);
            var serializedLayouts = ReadSerializedLayouts(package, packagePath, diagnostics);
            var pathLayoutIds = ReadPathLayoutIds(package, packagePath, diagnostics);
            var packageLayoutXml = ReadLayoutXml(package, serializedLayouts, diagnostics);
            var packageLayout = ParseLayoutOrEmpty(packageLayoutXml, diagnostics);

            foreach (var container in containers.OfType<TaskHost>())
            {
                var layoutId = containerLayoutIds.TryGetValue(container.ID, out var referenceId)
                    ? referenceId : container.ID;
                packageLayout.TryGetValue(layoutId, out var position);
                nodes.Add(new DesignerNode("package", container.ID, layoutId,
                    position));
            }

            ApplyPositions(nodes, packageLayout, "package");

            foreach (var task in containers.OfType<TaskHost>())
            {
                var pipeline = task.InnerObject as IDTSPipeline130;
                if (pipeline == null) { continue; }
                var taskLayoutId = containerLayoutIds.TryGetValue(task.ID, out var taskReferenceId)
                    ? taskReferenceId : "Package\\" + task.Name;

                var components = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().ToArray();
                var byOutput = new Dictionary<int, IDTSComponentMetaData100>();
                var byInput = new Dictionary<int, IDTSComponentMetaData100>();
                foreach (var component in components)
                {
                    var nativeId = component.ID.ToString(CultureInfo.InvariantCulture);
                    nodes.Add(new DesignerNode(task.ID, nativeId, taskLayoutId + "\\" + component.Name, null));
                    foreach (IDTSOutput100 output in component.OutputCollection)
                    {
                        byOutput[output.ID] = component;
                    }
                    foreach (IDTSInput100 input in component.InputCollection)
                    {
                        byInput[input.ID] = component;
                    }
                }

                var taskLayoutXml = ReadLayoutXml(task, serializedLayouts, diagnostics);
                if (string.IsNullOrWhiteSpace(taskLayoutXml)) { taskLayoutXml = packageLayoutXml; }
                var taskLayout = ParseLayoutOrEmpty(taskLayoutXml, diagnostics);
                var routePoints = ParseRoutePointsOrEmpty(taskLayoutXml, diagnostics);
                ApplyPositions(nodes, taskLayout, task.ID);
                foreach (IDTSPath100 path in pipeline.PathCollection)
                {
                    if (!byOutput.TryGetValue(path.StartPoint.ID, out var source) ||
                        !byInput.TryGetValue(path.EndPoint.ID, out var destination))
                    {
                        diagnostics.Add("designer.layout.path_endpoint_unresolved");
                        continue;
                    }
                    var nativeRouteId = path.ID.ToString(CultureInfo.InvariantCulture);
                    var sourceLayoutId = taskLayoutId + "\\" + source.Name;
                    var sourcePortLayoutId = sourceLayoutId + ".Outputs[" + path.StartPoint.Name + "]";
                    var destinationLayoutId = taskLayoutId + "\\" + destination.Name;
                    var destinationPortLayoutId = destinationLayoutId + ".Inputs[" + path.EndPoint.Name + "]";
                    var endpointKey = PathEndpointKey(sourcePortLayoutId, destinationPortLayoutId);
                    var routeLayoutId = pathLayoutIds.TryGetValue(endpointKey, out var savedPathId)
                        ? savedPathId : nativeRouteId;
                    var connectionLayoutId = taskLayoutId + ".Paths[" + sourcePortLayoutId + "->" + destinationPortLayoutId + "]";
                    var route = routePoints.TryGetValue(routeLayoutId, out var referenceRoute)
                        ? referenceRoute
                        : routePoints.TryGetValue(nativeRouteId, out var nativeRoute) ? nativeRoute : null;
                    connections.Add(new DesignerConnection(task.ID, path.ID.ToString(CultureInfo.InvariantCulture),
                        source.ID.ToString(CultureInfo.InvariantCulture), path.StartPoint.ID.ToString(CultureInfo.InvariantCulture),
                        destination.ID.ToString(CultureInfo.InvariantCulture), path.EndPoint.ID.ToString(CultureInfo.InvariantCulture),
                        route, connectionLayoutId, sourceLayoutId, sourcePortLayoutId, destinationLayoutId, destinationPortLayoutId));
                }
            }

            if (nodes.Any(node => !node.HasPosition)) { diagnostics.Add("designer.layout.position_unavailable"); }
            return new DesignerLayoutSnapshot(nodes, connections, diagnostics);
        }

        public static IReadOnlyDictionary<string, DesignerNodePosition> ParseLayout(string designTimePropertiesXml)
        {
            if (string.IsNullOrWhiteSpace(designTimePropertiesXml)) { return new ReadOnlyDictionary<string, DesignerNodePosition>(new Dictionary<string, DesignerNodePosition>()); }
            var document = ParseDocument(designTimePropertiesXml);

            var result = new Dictionary<string, DesignerNodePosition>(StringComparer.Ordinal);
            foreach (var element in document.Root.DescendantsAndSelf().Where(item => item.Name.LocalName == "NodeLayout"))
            {
                var id = Attribute(element, "Id");
                if (string.IsNullOrWhiteSpace(id)) { continue; }
                var position = ParsePosition(element);
                if (position == null) { continue; }
                if (result.ContainsKey(id)) { throw new InvalidOperationException("designer.layout.node_id_duplicate"); }
                result.Add(id, position);
            }
            return new ReadOnlyDictionary<string, DesignerNodePosition>(result);
        }

        public static IReadOnlyDictionary<string, IReadOnlyList<DesignerPoint>> ParseRoutePoints(string designTimePropertiesXml)
        {
            if (string.IsNullOrWhiteSpace(designTimePropertiesXml))
            { return new ReadOnlyDictionary<string, IReadOnlyList<DesignerPoint>>(new Dictionary<string, IReadOnlyList<DesignerPoint>>()); }
            var document = ParseDocument(designTimePropertiesXml);
            var result = new Dictionary<string, IReadOnlyList<DesignerPoint>>(StringComparer.Ordinal);
            foreach (var element in document.Root.DescendantsAndSelf().Where(item =>
                item.Name.LocalName == "EdgeLayout" || item.Name.LocalName == "PathLayout" || item.Name.LocalName == "ConnectionLayout"))
            {
                var id = Attribute(element, "Id");
                if (string.IsNullOrWhiteSpace(id)) { continue; }
                var points = ParseEdgeCurvePoints(element).ToArray();
                if (points.Length == 0)
                {
                    points = element.Elements().Where(point => point.Name.LocalName == "Point" || point.Name.LocalName == "Waypoint")
                    .Select(point => new { X = ParseDecimal(Attribute(point, "X")), Y = ParseDecimal(Attribute(point, "Y")) })
                    .Where(point => point.X.HasValue && point.Y.HasValue)
                    .Select(point => new DesignerPoint(point.X.Value, point.Y.Value)).ToArray();
                }
                if (points.Length == 0)
                {
                    points = ParsePoints(Attribute(element, "Points") ?? Attribute(element, "Route") ?? Attribute(element, "Waypoints")).ToArray();
                }
                if (points.Length > 0) { result[id] = new ReadOnlyCollection<DesignerPoint>(points); }
            }
            return new ReadOnlyDictionary<string, IReadOnlyList<DesignerPoint>>(result);
        }

        private static XDocument ParseDocument(string xml)
        {
            if (xml.Length > MaxLayoutCharacters) { throw new InvalidOperationException("designer.layout.size_limit"); }
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                    MaxCharactersInDocument = MaxLayoutCharacters, IgnoreComments = true };
                using (var text = new StringReader(xml))
                using (var reader = XmlReader.Create(text, settings)) { return XDocument.Load(reader); }
            }
            catch (Exception error) when (error is XmlException || error is InvalidOperationException)
            { throw new InvalidOperationException("designer.layout.xml_invalid"); }
        }

        private static IEnumerable<DesignerPoint> ParsePoints(string points)
        {
            if (string.IsNullOrWhiteSpace(points)) { yield break; }
            foreach (var point in points.Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = ParsePair(point);
                if (pair != null) { yield return new DesignerPoint(pair.Item1, pair.Item2); }
            }
        }

        private static IEnumerable<DesignerPoint> ParseEdgeCurvePoints(XElement edgeLayout)
        {
            var curve = edgeLayout.Descendants().FirstOrDefault(element => element.Name.LocalName == "Curve");
            if (curve == null) { yield break; }
            var start = ParsePair(Attribute(curve, "Start"));
            var offset = ParsePair(Attribute(edgeLayout, "TopLeft")) ?? Tuple.Create(0m, 0m);
            if (start == null) { yield break; }

            var currentX = start.Item1;
            var currentY = start.Item2;
            yield return new DesignerPoint(offset.Item1 + currentX, offset.Item2 + currentY);
            foreach (var segment in curve.Descendants().Where(element =>
                element.Name.LocalName == "LineSegment" || element.Name.LocalName == "CubicBezierSegment"))
            {
                if (segment.Name.LocalName == "LineSegment")
                {
                    var end = ParsePair(Attribute(segment, "End"));
                    if (end == null) { continue; }
                    currentX = end.Item1;
                    currentY = end.Item2;
                    yield return new DesignerPoint(offset.Item1 + currentX, offset.Item2 + currentY);
                    continue;
                }

                var firstControl = ParsePair(Attribute(segment, "Point1"));
                var secondControl = ParsePair(Attribute(segment, "Point2"));
                var endPoint = ParsePair(Attribute(segment, "Point3"));
                if (firstControl == null || secondControl == null || endPoint == null) { continue; }
                for (var sample = 1; sample <= 8; sample++)
                {
                    var t = sample / 8m;
                    var inverse = 1m - t;
                    var x = inverse * inverse * inverse * currentX +
                        3m * inverse * inverse * t * firstControl.Item1 +
                        3m * inverse * t * t * secondControl.Item1 + t * t * t * endPoint.Item1;
                    var y = inverse * inverse * inverse * currentY +
                        3m * inverse * inverse * t * firstControl.Item2 +
                        3m * inverse * t * t * secondControl.Item2 + t * t * t * endPoint.Item2;
                    yield return new DesignerPoint(offset.Item1 + x, offset.Item2 + y);
                }
                currentX = endPoint.Item1;
                currentY = endPoint.Item2;
            }
        }

        private static DesignerNodePosition ParsePosition(XElement element)
        {
            var location = ParsePair(Attribute(element, "TopLeft") ?? Attribute(element, "Location") ?? Attribute(element, "Position"));
            var size = ParsePair(Attribute(element, "Size"));
            var left = location?.Item1 ?? ParseDecimal(Attribute(element, "Left"));
            var top = location?.Item2 ?? ParseDecimal(Attribute(element, "Top"));
            var width = size?.Item1 ?? ParseDecimal(Attribute(element, "Width"));
            var height = size?.Item2 ?? ParseDecimal(Attribute(element, "Height"));
            if (!left.HasValue || !top.HasValue || !width.HasValue || !height.HasValue || width <= 0 || height <= 0) { return null; }
            return new DesignerNodePosition(left.Value, top.Value, width.Value, height.Value);
        }

        private static Tuple<decimal, decimal> ParsePair(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) { return null; }
            var parts = value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !decimal.TryParse(parts[0].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var first) ||
                !decimal.TryParse(parts[1].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var second)) { return null; }
            return Tuple.Create(first, second);
        }

        private static decimal? ParseDecimal(string value) => decimal.TryParse(value, NumberStyles.Number,
            CultureInfo.InvariantCulture, out var parsed) ? parsed : (decimal?)null;
        private static string Attribute(XElement element, string localName) =>
            (string)element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName);

        private static IReadOnlyDictionary<string, string> ReadSerializedLayouts(Package package, string packagePath,
            ICollection<string> diagnostics)
        {
            if (!string.IsNullOrWhiteSpace(packagePath))
            {
                var sourceLayouts = ReadSerializedLayoutsFromFile(packagePath, diagnostics);
                if (sourceLayouts.Count > 0) { return sourceLayouts; }
            }
            try
            {
                string packageXml;
                package.SaveToXML(out packageXml, null);
                if (packageXml.Length > MaxSerializedPackageCharacters)
                {
                    diagnostics.Add("designer.layout.package_size_limit");
                    return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
                }

                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                    MaxCharactersInDocument = MaxSerializedPackageCharacters, IgnoreComments = true };
                using (var text = new StringReader(packageXml))
                using (var reader = XmlReader.Create(text, settings))
                {
                    var document = XDocument.Load(reader);
                    var layouts = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var executable in document.Root.DescendantsAndSelf()
                        .Where(element => element.Name.LocalName == "Executable"))
                    {
                        var nativeId = Attribute(executable, "DTSID");
                        var layoutElement = executable.Elements().SingleOrDefault(element =>
                            element.Name.LocalName == "DesignTimeProperties");
                        var layoutXml = layoutElement?.Value;
                        if (!string.IsNullOrWhiteSpace(nativeId) && !string.IsNullOrWhiteSpace(layoutXml))
                        { layouts[nativeId] = layoutXml; }
                    }
                    return new ReadOnlyDictionary<string, string>(layouts);
                }
            }
            catch (Exception)
            {
                diagnostics.Add("designer.layout.serialized_metadata_unavailable");
                return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
            }
        }

        private static IReadOnlyDictionary<string, string> ReadSerializedLayoutsFromFile(string packagePath,
            ICollection<string> diagnostics)
        {
            try
            {
                if (new FileInfo(packagePath).Length > MaxSerializedPackageCharacters)
                {
                    diagnostics.Add("designer.layout.package_size_limit");
                    return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
                }
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                    MaxCharactersInDocument = MaxSerializedPackageCharacters, IgnoreComments = true };
                using (var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = XmlReader.Create(stream, settings))
                {
                    var document = XDocument.Load(reader);
                    var layouts = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var executable in document.Root.DescendantsAndSelf()
                        .Where(element => element.Name.LocalName == "Executable"))
                    {
                        var nativeId = Attribute(executable, "DTSID");
                        var layoutElement = executable.Elements().SingleOrDefault(element =>
                            element.Name.LocalName == "DesignTimeProperties");
                        var layoutXml = layoutElement?.Value;
                        if (!string.IsNullOrWhiteSpace(nativeId) && !string.IsNullOrWhiteSpace(layoutXml))
                        { layouts[nativeId] = layoutXml; }
                    }
                    return new ReadOnlyDictionary<string, string>(layouts);
                }
            }
            catch (Exception)
            {
                diagnostics.Add("designer.layout.source_metadata_unavailable");
                return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
            }
        }

        private static IReadOnlyDictionary<string, string> ReadPathLayoutIds(Package package, string packagePath,
            ICollection<string> diagnostics)
        {
            try
            {
                XDocument document;
                if (!string.IsNullOrWhiteSpace(packagePath))
                {
                    if (new FileInfo(packagePath).Length > MaxSerializedPackageCharacters)
                    { throw new InvalidOperationException("designer.layout.package_size_limit"); }
                    var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                        MaxCharactersInDocument = MaxSerializedPackageCharacters, IgnoreComments = true };
                    using (var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var reader = XmlReader.Create(stream, settings)) { document = XDocument.Load(reader); }
                }
                else
                {
                    package.SaveToXML(out var xml, null);
                    if (xml.Length > MaxSerializedPackageCharacters)
                    { throw new InvalidOperationException("designer.layout.package_size_limit"); }
                    document = XDocument.Parse(xml);
                }

                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var path in document.Descendants().Where(element => element.Name.LocalName == "path"))
                {
                    var referenceId = Attribute(path, "refId");
                    var startId = Attribute(path, "startId");
                    var endId = Attribute(path, "endId");
                    if (string.IsNullOrWhiteSpace(referenceId) || string.IsNullOrWhiteSpace(startId) ||
                        string.IsNullOrWhiteSpace(endId)) { continue; }
                    var endpointKey = PathEndpointKey(startId, endId);
                    if (result.ContainsKey(endpointKey))
                    {
                        diagnostics.Add("designer.layout.path_identity_ambiguous");
                        result.Remove(endpointKey);
                        continue;
                    }
                    result.Add(endpointKey, referenceId);
                }
                return new ReadOnlyDictionary<string, string>(result);
            }
            catch (Exception error) when (error is XmlException || error is IOException ||
                error is UnauthorizedAccessException || error is InvalidOperationException)
            {
                diagnostics.Add(error is InvalidOperationException && error.Message == "designer.layout.package_size_limit"
                    ? error.Message : "designer.layout.path_metadata_unavailable");
                return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
            }
        }

        private static string PathEndpointKey(string startId, string endId) => startId + "\u001f" + endId;

        private static string ReadLayoutXml(DtsContainer owner, IReadOnlyDictionary<string, string> serializedLayouts,
            ICollection<string> diagnostics)
        {
            try
            {
                if (serializedLayouts.TryGetValue(owner.ID, out var sourceXml)) { return sourceXml; }
                if (owner is Package layoutPackage && !string.IsNullOrWhiteSpace(layoutPackage.DesignTimeProperties))
                { return layoutPackage.DesignTimeProperties; }
                var properties = owner is Package package ? package.Properties : (owner as TaskHost)?.Properties;
                if (properties == null)
                {
                    if (serializedLayouts.Count == 0) { diagnostics.Add("designer.layout.owner_properties_unavailable"); }
                    return string.Empty;
                }
                var property = properties.Cast<DtsProperty>().SingleOrDefault(item =>
                    string.Equals(item.Name, "DesignTimeProperties", StringComparison.OrdinalIgnoreCase));
                if (property == null)
                {
                    if (serializedLayouts.Count == 0) { diagnostics.Add("designer.layout.property_missing"); }
                    return string.Empty;
                }
                var xml = property.GetValue(owner) as string;
                if (string.IsNullOrWhiteSpace(xml))
                {
                    if (serializedLayouts.Count == 0) { diagnostics.Add("designer.layout.property_empty"); }
                    return string.Empty;
                }
                return xml;
            }
            catch (Exception)
            {
                diagnostics.Add("designer.layout.read_failed");
                return string.Empty;
            }
        }

        private static IReadOnlyDictionary<string, DesignerNodePosition> ParseLayoutOrEmpty(
            string xml, ICollection<string> diagnostics)
        {
            try { return ParseLayout(xml); }
            catch (InvalidOperationException error)
            {
                diagnostics.Add(error.Message);
                return new ReadOnlyDictionary<string, DesignerNodePosition>(new Dictionary<string, DesignerNodePosition>());
            }
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<DesignerPoint>> ParseRoutePointsOrEmpty(
            string xml, ICollection<string> diagnostics)
        {
            try { return ParseRoutePoints(xml); }
            catch (InvalidOperationException error)
            {
                diagnostics.Add(error.Message);
                return new ReadOnlyDictionary<string, IReadOnlyList<DesignerPoint>>(
                    new Dictionary<string, IReadOnlyList<DesignerPoint>>());
            }
        }

        private static void ApplyPositions(IList<DesignerNode> nodes,
            IReadOnlyDictionary<string, DesignerNodePosition> positions, string ownerNativeId)
        {
            for (var index = 0; index < nodes.Count; index++)
            {
                var node = nodes[index];
                var hasPosition = positions.TryGetValue(node.LayoutId, out var position) ||
                    positions.TryGetValue(node.NativeId, out position);
                if (node.OwnerNativeId == ownerNativeId && hasPosition)
                {
                    nodes[index] = new DesignerNode(node.OwnerNativeId, node.NativeId, node.LayoutId, position);
                }
            }
        }

        private static IEnumerable<DtsContainer> Containers(Package package)
        {
            foreach (Executable executable in package.Executables)
            {
                foreach (var container in Containers((DtsContainer)executable)) { yield return container; }
            }
            foreach (DtsEventHandler handler in package.EventHandlers)
            {
                foreach (Executable executable in handler.Executables)
                {
                    foreach (var container in Containers((DtsContainer)executable)) { yield return container; }
                }
            }
        }

        private static IReadOnlyDictionary<string, string> ContainerLayoutIds(Package package)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Executable executable in package.Executables)
            {
                AddContainerLayoutIds((DtsContainer)executable, "Package", result);
            }
            return result;
        }

        private static void AddContainerLayoutIds(DtsContainer container, string parentLayoutId,
            IDictionary<string, string> layoutIds)
        {
            var layoutId = parentLayoutId + "\\" + container.Name;
            layoutIds[container.ID] = layoutId;
            if (container is IDTSSequence sequence)
            {
                foreach (Executable executable in sequence.Executables)
                {
                    AddContainerLayoutIds((DtsContainer)executable, layoutId, layoutIds);
                }
            }
        }

        private static IEnumerable<DtsContainer> Containers(DtsContainer container)
        {
            yield return container;
            if (container is IDTSSequence sequence)
            {
                foreach (Executable executable in sequence.Executables)
                {
                    foreach (var child in Containers((DtsContainer)executable)) { yield return child; }
                }
            }
            if (container is EventsProvider eventsProvider)
            {
                foreach (DtsEventHandler handler in eventsProvider.EventHandlers)
                {
                    foreach (Executable executable in handler.Executables)
                    {
                        foreach (var child in Containers((DtsContainer)executable)) { yield return child; }
                    }
                }
            }
        }
    }

    public sealed class DesignerLayoutWriter
    {
        private const int MaxLayoutCharacters = 1024 * 1024;
        private const string GraphLayoutNamespace = "clr-namespace:Microsoft.SqlServer.IntegrationServices.Designer.Model.Serialization;assembly=Microsoft.SqlServer.IntegrationServices.Graph";

        public void SetComponentPosition(Package package, string ownerTaskNativeId, string componentNativeId,
            DesignerPlacement placement)
        {
            if (package == null) { throw new ArgumentNullException(nameof(package)); }
            if (string.IsNullOrWhiteSpace(ownerTaskNativeId)) { throw new ArgumentException("An owner task ID is required.", nameof(ownerTaskNativeId)); }
            if (string.IsNullOrWhiteSpace(componentNativeId)) { throw new ArgumentException("A component ID is required.", nameof(componentNativeId)); }
            if (placement == null) { throw new ArgumentNullException(nameof(placement)); }
            if (!string.Equals(placement.OwnerNativeId, ownerTaskNativeId, StringComparison.Ordinal))
            { throw new InvalidOperationException("designer.placement.owner_mismatch"); }

            var tasks = FindTaskHosts(package).Where(task => task.ID == ownerTaskNativeId).ToArray();
            if (tasks.Length != 1) { throw new InvalidOperationException("designer.layout.owner_task_not_found"); }
            var task = tasks[0];
            var pipeline = task.InnerObject as IDTSPipeline130;
            if (pipeline == null) { throw new InvalidOperationException("designer.layout.owner_not_pipeline"); }
            var components = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                .Where(component => component.ID.ToString(CultureInfo.InvariantCulture) == componentNativeId).ToArray();
            if (components.Length != 1) { throw new InvalidOperationException("designer.layout.component_not_found"); }

            var taskLayoutId = ContainerLayoutIds(package).TryGetValue(task.ID, out var referenceId)
                ? referenceId : "Package\\" + task.Name;
            var componentLayoutId = componentNativeId;
            var xml = package.DesignTimeProperties;
            var document = ParseLayoutDocument(xml);
            var taskLayouts = document.Root.DescendantsAndSelf().Where(element =>
                element.Name.LocalName == "TaskHost" &&
                (string.Equals((string)element.Attribute("design-time-name"), task.ID, StringComparison.Ordinal) ||
                 string.Equals((string)element.Attribute("design-time-name"), taskLayoutId, StringComparison.Ordinal))).ToArray();
            if (taskLayouts.Length > 1) { throw new InvalidOperationException("designer.layout.owner_task_ambiguous"); }
            var taskLayout = taskLayouts.SingleOrDefault();
            if (taskLayout == null)
            {
                taskLayout = new XElement(document.Root.Name.Namespace + "TaskHost",
                    new XAttribute("design-time-name", task.ID));
                document.Root.Add(taskLayout);
            }
            var layoutInfo = taskLayout.Elements().SingleOrDefault(element => element.Name.LocalName == "LayoutInfo");
            if (layoutInfo == null)
            {
                layoutInfo = new XElement(taskLayout.Name.Namespace + "LayoutInfo");
                taskLayout.Add(layoutInfo);
            }
            var graphLayout = layoutInfo.Elements().SingleOrDefault(element => element.Name.LocalName == "GraphLayout");
            if (graphLayout == null)
            {
                graphLayout = new XElement(layoutInfo.Name.Namespace + "GraphLayout",
                    new XAttribute("Capacity", "4"),
                    new XAttribute(XNamespace.Xmlns + "clr-namespace", GraphLayoutNamespace));
                layoutInfo.Add(graphLayout);
            }

            var matches = graphLayout.Elements().Where(element => element.Name.LocalName == "NodeLayout" &&
                string.Equals((string)element.Attribute("Id"), componentLayoutId, StringComparison.Ordinal)).ToArray();
            if (matches.Length > 1) { throw new InvalidOperationException("designer.layout.node_id_duplicate"); }
            var nodeLayout = matches.SingleOrDefault();
            if (nodeLayout == null)
            {
                nodeLayout = new XElement(graphLayout.Name.Namespace + "NodeLayout",
                    new XAttribute("Id", componentLayoutId));
                graphLayout.Add(nodeLayout);
            }
            nodeLayout.SetAttributeValue("TopLeft", FormatPair(placement.Position.Left, placement.Position.Top));
            nodeLayout.SetAttributeValue("Size", FormatPair(placement.Position.Width, placement.Position.Height));
            package.DesignTimeProperties = document.ToString(SaveOptions.DisableFormatting);
        }

        private static XDocument ParseLayoutDocument(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return new XDocument(new XElement("Objects", new XAttribute("Version", "8"),
                    new XElement("Package", new XAttribute("design-time-name", "Package"))));
            }
            if (xml.Length > MaxLayoutCharacters) { throw new InvalidOperationException("designer.layout.size_limit"); }
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                    MaxCharactersInDocument = MaxLayoutCharacters, IgnoreComments = true };
                using (var text = new StringReader(xml))
                using (var reader = XmlReader.Create(text, settings)) { return XDocument.Load(reader); }
            }
            catch (Exception error) when (error is XmlException || error is InvalidOperationException)
            { throw new InvalidOperationException("designer.layout.xml_invalid"); }
        }

        private static string FormatPair(decimal first, decimal second) =>
            first.ToString("0.############################", CultureInfo.InvariantCulture) + "," +
            second.ToString("0.############################", CultureInfo.InvariantCulture);

        private static IEnumerable<TaskHost> FindTaskHosts(Package package)
        {
            foreach (Executable executable in package.Executables)
            {
                foreach (var task in FindTaskHosts((DtsContainer)executable)) { yield return task; }
            }
        }

        private static IEnumerable<TaskHost> FindTaskHosts(DtsContainer container)
        {
            if (container is TaskHost task) { yield return task; }
            if (container is IDTSSequence sequence)
            {
                foreach (Executable executable in sequence.Executables)
                {
                    foreach (var child in FindTaskHosts((DtsContainer)executable)) { yield return child; }
                }
            }
        }

        private static IReadOnlyDictionary<string, string> ContainerLayoutIds(Package package)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Executable executable in package.Executables)
            {
                AddContainerLayoutIds((DtsContainer)executable, "Package", result);
            }
            return result;
        }

        private static void AddContainerLayoutIds(DtsContainer container, string parentLayoutId,
            IDictionary<string, string> layoutIds)
        {
            var layoutId = parentLayoutId + "\\" + container.Name;
            layoutIds[container.ID] = layoutId;
            if (container is IDTSSequence sequence)
            {
                foreach (Executable executable in sequence.Executables)
                {
                    AddContainerLayoutIds((DtsContainer)executable, layoutId, layoutIds);
                }
            }
        }
    }

    public enum ComponentPlacementRelation
    {
        RightOf,
        Below,
        OpenSpace
    }

    public sealed class ComponentPlacementIntent
    {
        public ComponentPlacementIntent(string ownerNativeId, ComponentPlacementRelation relation,
            string anchorNativeId, int width, int height)
        {
            if (string.IsNullOrWhiteSpace(ownerNativeId)) { throw new ArgumentException("An owner native ID is required.", nameof(ownerNativeId)); }
            if (!Enum.IsDefined(typeof(ComponentPlacementRelation), relation)) { throw new ArgumentOutOfRangeException(nameof(relation)); }
            if (relation != ComponentPlacementRelation.OpenSpace && string.IsNullOrWhiteSpace(anchorNativeId))
            { throw new ArgumentException("An anchor component ID is required for relative placement.", nameof(anchorNativeId)); }
            if (width <= 0 || height <= 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
            OwnerNativeId = ownerNativeId;
            Relation = relation;
            AnchorNativeId = anchorNativeId ?? string.Empty;
            Width = width;
            Height = height;
        }

        public string OwnerNativeId { get; }
        public ComponentPlacementRelation Relation { get; }
        public string AnchorNativeId { get; }
        public int Width { get; }
        public int Height { get; }
    }

    public sealed class ComponentPlacementPreview
    {
        internal ComponentPlacementPreview(ComponentPlacementIntent intent, DesignerPlacement placement, string refusalCode)
        {
            Intent = intent;
            Placement = placement;
            RefusalCode = refusalCode ?? string.Empty;
        }

        public ComponentPlacementIntent Intent { get; }
        public DesignerPlacement Placement { get; }
        public string RefusalCode { get; }
        public bool IsValid => Placement != null && RefusalCode.Length == 0;
    }

    public sealed class DesignerPlacementPreviewer
    {
        private readonly DesignerLayoutExtractor extractor = new DesignerLayoutExtractor();
        private readonly DesignerLayoutService placementService = new DesignerLayoutService();

        public ComponentPlacementPreview Preview(Package package, ComponentPlacementIntent intent, string packagePath = null)
        {
            if (package == null) { throw new ArgumentNullException(nameof(package)); }
            if (intent == null) { throw new ArgumentNullException(nameof(intent)); }
            try
            {
                var snapshot = extractor.Extract(package, packagePath);
                DesignerPlacement placement;
                switch (intent.Relation)
                {
                    case ComponentPlacementRelation.RightOf:
                        placement = placementService.PlaceRightOf(snapshot, intent.OwnerNativeId,
                            intent.AnchorNativeId, intent.Width, intent.Height);
                        break;
                    case ComponentPlacementRelation.Below:
                        placement = placementService.PlaceBelow(snapshot, intent.OwnerNativeId,
                            intent.AnchorNativeId, intent.Width, intent.Height);
                        break;
                    case ComponentPlacementRelation.OpenSpace:
                        placement = placementService.FindOpenSpace(snapshot, intent.OwnerNativeId,
                            intent.Width, intent.Height);
                        break;
                    default:
                        throw new InvalidOperationException("designer.placement.intent_invalid");
                }
                return new ComponentPlacementPreview(intent, placement, string.Empty);
            }
            catch (InvalidOperationException error)
            {
                return new ComponentPlacementPreview(intent, null, error.Message);
            }
        }
    }

    public sealed class DesignerLayoutService
    {
        public DesignerPlacement PlaceRightOf(DesignerLayoutSnapshot snapshot, string ownerNativeId,
            string nativeId, int width, int height, int gap = 32)
        {
            ValidateDimensions(width, height, gap);
            var anchor = FindNode(snapshot, ownerNativeId, nativeId);
            RequireKnownPositions(snapshot, ownerNativeId);
            return Place(snapshot, ownerNativeId, checked(anchor.Position.Right + gap), anchor.Position.Top, width, height);
        }

        public DesignerPlacement PlaceBelow(DesignerLayoutSnapshot snapshot, string ownerNativeId,
            string nativeId, int width, int height, int gap = 32)
        {
            ValidateDimensions(width, height, gap);
            var anchor = FindNode(snapshot, ownerNativeId, nativeId);
            RequireKnownPositions(snapshot, ownerNativeId);
            return Place(snapshot, ownerNativeId, anchor.Position.Left, checked(anchor.Position.Bottom + gap), width, height);
        }

        public DesignerPlacement FindOpenSpace(DesignerLayoutSnapshot snapshot, string ownerNativeId, int width, int height, int gap = 32)
        {
            if (snapshot == null) { throw new ArgumentNullException(nameof(snapshot)); }
            ValidateDimensions(width, height, gap);
            RequireKnownPositions(snapshot, ownerNativeId);
            if (!snapshot.Nodes.Any(node => node.OwnerNativeId == ownerNativeId))
            { return new DesignerPlacement(ownerNativeId, gap, gap, width, height); }

            var bounds = snapshot.GetCanvasBounds(ownerNativeId);
            var startX = checked(bounds.Left + gap);
            var startY = checked(bounds.Top + gap);
            var scanWidth = Math.Max(800, checked(bounds.Width + width + gap * 2));
            var scanHeight = Math.Max(600, checked(bounds.Height + height + gap * 2));
            const int step = 32;
            for (var y = startY; y <= checked(startY + scanHeight); y = checked(y + step))
            {
                for (var x = startX; x <= checked(startX + scanWidth); x = checked(x + step))
                {
                    if (!Overlaps(snapshot, ownerNativeId, x, y, width, height))
                    { return new DesignerPlacement(ownerNativeId, x, y, width, height); }
                }
            }
            throw new InvalidOperationException("designer.placement.open_space_not_found");
        }

        public DesignerLayoutSnapshot Normalize(DesignerLayoutSnapshot snapshot, string ownerNativeId, int margin = 32)
        {
            if (snapshot == null) { throw new ArgumentNullException(nameof(snapshot)); }
            if (string.IsNullOrWhiteSpace(ownerNativeId)) { throw new ArgumentException("An owner native ID is required.", nameof(ownerNativeId)); }
            if (margin < 0) { throw new ArgumentOutOfRangeException(nameof(margin)); }
            RequireKnownPositions(snapshot, ownerNativeId);
            var ownerNodes = snapshot.Nodes.Where(node => node.OwnerNativeId == ownerNativeId).ToArray();
            if (ownerNodes.Length == 0) { return snapshot; }
            var shiftX = checked(margin - ownerNodes.Min(node => node.Position.Left));
            var shiftY = checked(margin - ownerNodes.Min(node => node.Position.Top));
            var nodes = snapshot.Nodes.Select(node => node.OwnerNativeId != ownerNativeId ? node :
                new DesignerNode(node.OwnerNativeId, node.NativeId, node.LayoutId,
                    new DesignerNodePosition(checked(node.Position.Left + shiftX), checked(node.Position.Top + shiftY),
                        node.Position.Width, node.Position.Height)));
            var connections = snapshot.Connections.Select(connection => connection.OwnerNativeId != ownerNativeId ? connection :
                new DesignerConnection(connection.OwnerNativeId, connection.NativeId, connection.SourceNativeId,
                    connection.SourcePortNativeId, connection.DestinationNativeId, connection.DestinationPortNativeId,
                    connection.RoutePoints.Select(point => new DesignerPoint(checked(point.X + shiftX), checked(point.Y + shiftY))),
                    connection.LayoutId, connection.SourceLayoutId, connection.SourcePortLayoutId,
                    connection.DestinationLayoutId, connection.DestinationPortLayoutId));
            return new DesignerLayoutSnapshot(nodes, connections, snapshot.Diagnostics, margin);
        }

        private static DesignerPlacement Place(DesignerLayoutSnapshot snapshot, string ownerNativeId,
            decimal left, decimal top, int width, int height)
        {
            ValidateDimensions(width, height, 0);
            if (Overlaps(snapshot, ownerNativeId, left, top, width, height))
            { throw new InvalidOperationException("designer.placement.position_occupied"); }
            return new DesignerPlacement(ownerNativeId, left, top, width, height);
        }

        private static DesignerNode FindNode(DesignerLayoutSnapshot snapshot, string ownerNativeId, string nativeId)
        {
            if (snapshot == null) { throw new ArgumentNullException(nameof(snapshot)); }
            var node = snapshot.Nodes.SingleOrDefault(item => item.OwnerNativeId == ownerNativeId && item.NativeId == nativeId);
            if (node == null) { throw new InvalidOperationException("designer.placement.target_not_found"); }
            if (!node.HasPosition) { throw new InvalidOperationException("designer.layout.position_unavailable"); }
            return node;
        }

        private static void RequireKnownPositions(DesignerLayoutSnapshot snapshot, string ownerNativeId)
        {
            if (snapshot.Nodes.Any(node => node.OwnerNativeId == ownerNativeId && !node.HasPosition))
            { throw new InvalidOperationException("designer.layout.position_unavailable"); }
        }

        private static bool Overlaps(DesignerLayoutSnapshot snapshot, string ownerNativeId, decimal left, decimal top, int width, int height)
        {
            var right = checked(left + width);
            var bottom = checked(top + height);
            if (snapshot.Nodes.Where(node => node.OwnerNativeId == ownerNativeId && node.HasPosition).Any(node => left < node.Position.Right && right > node.Position.Left &&
                top < node.Position.Bottom && bottom > node.Position.Top)) { return true; }
            const int routeClearance = 8;
            return snapshot.Connections.Where(connection => connection.OwnerNativeId == ownerNativeId)
                .SelectMany(connection => connection.RoutePoints.Zip(connection.RoutePoints.Skip(1),
                    (first, second) => new { First = first, Second = second }))
                .Any(segment => left < checked(Math.Max(segment.First.X, segment.Second.X) + routeClearance) &&
                    right > checked(Math.Min(segment.First.X, segment.Second.X) - routeClearance) &&
                    top < checked(Math.Max(segment.First.Y, segment.Second.Y) + routeClearance) &&
                    bottom > checked(Math.Min(segment.First.Y, segment.Second.Y) - routeClearance));
        }

        private static void ValidateDimensions(int width, int height, int gap)
        {
            if (width <= 0 || height <= 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
            if (gap < 0) { throw new ArgumentOutOfRangeException(nameof(gap)); }
        }
    }

    public sealed class DesignerLayoutComparer
    {
        public DesignerLayoutComparison Compare(DesignerLayoutSnapshot expected, DesignerLayoutSnapshot actual)
        {
            if (expected == null) { throw new ArgumentNullException(nameof(expected)); }
            if (actual == null) { throw new ArgumentNullException(nameof(actual)); }
            var differences = new List<string>();
            var actualNodes = actual.Nodes.ToDictionary(node => node.Key, StringComparer.Ordinal);
            foreach (var expectedNode in expected.Nodes)
            {
                if (!actualNodes.TryGetValue(expectedNode.Key, out var actualNode))
                { differences.Add("designer.layout.node_missing"); continue; }
                if (!Equals(expectedNode.Position, actualNode.Position)) { differences.Add("designer.layout.node_position_changed"); }
            }
            if (expected.Nodes.Count != actualNodes.Count || expected.Nodes.Any(node => !actualNodes.ContainsKey(node.Key)))
            { differences.Add("designer.layout.node_inventory_changed"); }

            var expectedConnections = expected.Connections.ToDictionary(connection => connection.Key, StringComparer.Ordinal);
            var actualConnections = actual.Connections.ToDictionary(connection => connection.Key, StringComparer.Ordinal);
            if (expectedConnections.Count != actualConnections.Count || expectedConnections.Keys.Except(actualConnections.Keys, StringComparer.Ordinal).Any())
            { differences.Add("designer.layout.connection_inventory_changed"); }
            foreach (var pair in expectedConnections)
            {
                if (!actualConnections.TryGetValue(pair.Key, out var connection)) { continue; }
                if (!SameIdentity(pair.Value.SourceNativeId, pair.Value.SourceLayoutId, connection.SourceNativeId, connection.SourceLayoutId) ||
                    !SameIdentity(pair.Value.SourcePortNativeId, pair.Value.SourcePortLayoutId, connection.SourcePortNativeId, connection.SourcePortLayoutId) ||
                    !SameIdentity(pair.Value.DestinationNativeId, pair.Value.DestinationLayoutId, connection.DestinationNativeId, connection.DestinationLayoutId) ||
                    !SameIdentity(pair.Value.DestinationPortNativeId, pair.Value.DestinationPortLayoutId, connection.DestinationPortNativeId, connection.DestinationPortLayoutId) ||
                    !pair.Value.RoutePoints.SequenceEqual(connection.RoutePoints))
                { differences.Add("designer.layout.connection_changed"); }
            }
            return new DesignerLayoutComparison(differences);
        }

        public DesignerLayoutComparison ComparePreservingExisting(DesignerLayoutSnapshot before,
            DesignerLayoutSnapshot after, IEnumerable<string> explicitlyMovedNodeKeys = null,
            IEnumerable<string> explicitlyReplacedConnectionKeys = null)
        {
            if (before == null) { throw new ArgumentNullException(nameof(before)); }
            if (after == null) { throw new ArgumentNullException(nameof(after)); }
            var allowedMoves = new HashSet<string>(explicitlyMovedNodeKeys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var allowedConnectionReplacements = new HashSet<string>(
                explicitlyReplacedConnectionKeys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var afterNodes = after.Nodes.ToDictionary(node => node.Key, StringComparer.Ordinal);
            var differences = new List<string>();
            foreach (var node in before.Nodes)
            {
                if (!afterNodes.TryGetValue(node.Key, out var afterNode)) { differences.Add("designer.layout.existing_node_missing"); }
                else if (!allowedMoves.Contains(node.Key) && !Equals(node.Position, afterNode.Position))
                { differences.Add("designer.layout.existing_node_moved"); }
            }

            var afterConnections = after.Connections.ToDictionary(connection => connection.Key, StringComparer.Ordinal);
            foreach (var connection in before.Connections)
            {
                if (allowedConnectionReplacements.Contains(connection.Key)) { continue; }
                if (!afterConnections.TryGetValue(connection.Key, out var afterConnection))
                { differences.Add("designer.layout.existing_connection_missing"); }
                else if (!SameIdentity(connection.SourceNativeId, connection.SourceLayoutId,
                        afterConnection.SourceNativeId, afterConnection.SourceLayoutId) ||
                    !SameIdentity(connection.SourcePortNativeId, connection.SourcePortLayoutId,
                        afterConnection.SourcePortNativeId, afterConnection.SourcePortLayoutId) ||
                    !SameIdentity(connection.DestinationNativeId, connection.DestinationLayoutId,
                        afterConnection.DestinationNativeId, afterConnection.DestinationLayoutId) ||
                    !SameIdentity(connection.DestinationPortNativeId, connection.DestinationPortLayoutId,
                        afterConnection.DestinationPortNativeId, afterConnection.DestinationPortLayoutId) ||
                    !connection.RoutePoints.SequenceEqual(afterConnection.RoutePoints))
                { differences.Add("designer.layout.existing_connection_changed"); }
            }
            return new DesignerLayoutComparison(differences);
        }

        private static bool SameIdentity(string firstNativeId, string firstLayoutId,
            string secondNativeId, string secondLayoutId) =>
            !string.IsNullOrEmpty(firstLayoutId) && !string.IsNullOrEmpty(secondLayoutId)
                ? string.Equals(firstLayoutId, secondLayoutId, StringComparison.Ordinal)
                : string.Equals(firstNativeId, secondNativeId, StringComparison.Ordinal);
    }
}