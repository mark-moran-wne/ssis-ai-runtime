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
        public DesignerNodePosition(int left, int top, int width, int height)
        {
            if (width <= 0 || height <= 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public int Left { get; }
        public int Top { get; }
        public int Width { get; }
        public int Height { get; }
        public int Right => checked(Left + Width);
        public int Bottom => checked(Top + Height);

        public bool Equals(DesignerNodePosition other) => other != null && Left == other.Left && Top == other.Top &&
            Width == other.Width && Height == other.Height;
        public override bool Equals(object obj) => Equals(obj as DesignerNodePosition);
        public override int GetHashCode() => (((Left * 397) ^ Top) * 397 ^ Width) * 397 ^ Height;
    }

    public sealed class DesignerPoint : IEquatable<DesignerPoint>
    {
        public DesignerPoint(int x, int y) { X = x; Y = y; }
        public int X { get; }
        public int Y { get; }
        public bool Equals(DesignerPoint other) => other != null && X == other.X && Y == other.Y;
        public override bool Equals(object obj) => Equals(obj as DesignerPoint);
        public override int GetHashCode() => (X * 397) ^ Y;
    }

    public sealed class DesignerBounds : IEquatable<DesignerBounds>
    {
        public DesignerBounds(int left, int top, int width, int height)
        {
            if (width < 0 || height < 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public int Left { get; }
        public int Top { get; }
        public int Width { get; }
        public int Height { get; }
        public int Right => checked(Left + Width);
        public int Bottom => checked(Top + Height);
        public bool Equals(DesignerBounds other) => other != null && Left == other.Left && Top == other.Top &&
            Width == other.Width && Height == other.Height;
        public override bool Equals(object obj) => Equals(obj as DesignerBounds);
        public override int GetHashCode() => (((Left * 397) ^ Top) * 397 ^ Width) * 397 ^ Height;
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
        public string Key => OwnerNativeId + "\u001f" + NativeId;
    }

    public sealed class DesignerConnection
    {
        public DesignerConnection(string ownerNativeId, string nativeId, string sourceNativeId,
            string sourcePortNativeId, string destinationNativeId, string destinationPortNativeId,
            IEnumerable<DesignerPoint> routePoints = null)
        {
            OwnerNativeId = ownerNativeId ?? throw new ArgumentNullException(nameof(ownerNativeId));
            NativeId = nativeId ?? throw new ArgumentNullException(nameof(nativeId));
            SourceNativeId = sourceNativeId ?? throw new ArgumentNullException(nameof(sourceNativeId));
            SourcePortNativeId = sourcePortNativeId ?? throw new ArgumentNullException(nameof(sourcePortNativeId));
            DestinationNativeId = destinationNativeId ?? throw new ArgumentNullException(nameof(destinationNativeId));
            DestinationPortNativeId = destinationPortNativeId ?? throw new ArgumentNullException(nameof(destinationPortNativeId));
            RoutePoints = new ReadOnlyCollection<DesignerPoint>((routePoints ?? Enumerable.Empty<DesignerPoint>()).ToArray());
        }

        public string OwnerNativeId { get; }
        public string NativeId { get; }
        public string SourceNativeId { get; }
        public string SourcePortNativeId { get; }
        public string DestinationNativeId { get; }
        public string DestinationPortNativeId { get; }
        public IReadOnlyList<DesignerPoint> RoutePoints { get; }
        public string Key => OwnerNativeId + "\u001f" + NativeId;
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
        public DesignerPlacement(string ownerNativeId, int left, int top, int width, int height)
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

        public DesignerLayoutSnapshot Extract(Package package)
        {
            if (package == null) { throw new ArgumentNullException(nameof(package)); }
            var nodes = new List<DesignerNode>();
            var connections = new List<DesignerConnection>();
            var diagnostics = new List<string>();
            var containers = Containers(package).ToArray();
            var packageLayout = ParseLayoutOrEmpty(ReadLayoutXml(package, diagnostics), diagnostics);

            foreach (var container in containers.OfType<TaskHost>())
            {
                packageLayout.TryGetValue(container.ID, out var position);
                nodes.Add(new DesignerNode("package", container.ID, container.ID,
                    position));
            }

            ApplyPositions(nodes, packageLayout, "package");

            foreach (var task in containers.OfType<TaskHost>())
            {
                var pipeline = task.InnerObject as IDTSPipeline130;
                if (pipeline == null) { continue; }

                var components = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().ToArray();
                var byOutput = new Dictionary<int, IDTSComponentMetaData100>();
                var byInput = new Dictionary<int, IDTSComponentMetaData100>();
                foreach (var component in components)
                {
                    var nativeId = component.ID.ToString(CultureInfo.InvariantCulture);
                    nodes.Add(new DesignerNode(task.ID, nativeId, nativeId, null));
                    foreach (IDTSOutput100 output in component.OutputCollection)
                    {
                        byOutput[output.ID] = component;
                    }
                    foreach (IDTSInput100 input in component.InputCollection)
                    {
                        byInput[input.ID] = component;
                    }
                }

                var taskLayoutXml = ReadLayoutXml(task, diagnostics);
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
                    connections.Add(new DesignerConnection(task.ID, path.ID.ToString(CultureInfo.InvariantCulture),
                        source.ID.ToString(CultureInfo.InvariantCulture), path.StartPoint.ID.ToString(CultureInfo.InvariantCulture),
                        destination.ID.ToString(CultureInfo.InvariantCulture), path.EndPoint.ID.ToString(CultureInfo.InvariantCulture),
                        routePoints.TryGetValue(path.ID.ToString(CultureInfo.InvariantCulture), out var points) ? points : null));
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
                var points = element.Elements().Where(point => point.Name.LocalName == "Point" || point.Name.LocalName == "Waypoint")
                    .Select(point => new { X = ParseInt(Attribute(point, "X")), Y = ParseInt(Attribute(point, "Y")) })
                    .Where(point => point.X.HasValue && point.Y.HasValue)
                    .Select(point => new DesignerPoint(point.X.Value, point.Y.Value)).ToArray();
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

        private static DesignerNodePosition ParsePosition(XElement element)
        {
            var location = ParsePair(Attribute(element, "TopLeft") ?? Attribute(element, "Location") ?? Attribute(element, "Position"));
            var size = ParsePair(Attribute(element, "Size"));
            var left = location?.Item1 ?? ParseInt(Attribute(element, "Left"));
            var top = location?.Item2 ?? ParseInt(Attribute(element, "Top"));
            var width = size?.Item1 ?? ParseInt(Attribute(element, "Width"));
            var height = size?.Item2 ?? ParseInt(Attribute(element, "Height"));
            if (!left.HasValue || !top.HasValue || !width.HasValue || !height.HasValue || width <= 0 || height <= 0) { return null; }
            return new DesignerNodePosition(left.Value, top.Value, width.Value, height.Value);
        }

        private static Tuple<int, int> ParsePair(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) { return null; }
            var parts = value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var first) ||
                !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var second)) { return null; }
            return Tuple.Create(first, second);
        }

        private static int? ParseInt(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : (int?)null;
        private static string Attribute(XElement element, string localName) =>
            (string)element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName);

        private static string ReadLayoutXml(DtsContainer owner, ICollection<string> diagnostics)
        {
            try
            {
                var properties = owner is Package package ? package.Properties : (owner as TaskHost)?.Properties;
                if (properties == null) { return string.Empty; }
                var property = properties.Cast<DtsProperty>().SingleOrDefault(item =>
                    string.Equals(item.Name, "DesignTimeProperties", StringComparison.OrdinalIgnoreCase));
                if (property == null) { return string.Empty; }
                return property.GetValue(owner) as string ?? string.Empty;
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
                if (node.OwnerNativeId == ownerNativeId && positions.TryGetValue(node.LayoutId, out var position))
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
                    connection.RoutePoints.Select(point => new DesignerPoint(checked(point.X + shiftX), checked(point.Y + shiftY)))));
            return new DesignerLayoutSnapshot(nodes, connections, snapshot.Diagnostics, margin);
        }

        private static DesignerPlacement Place(DesignerLayoutSnapshot snapshot, string ownerNativeId,
            int left, int top, int width, int height)
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

        private static bool Overlaps(DesignerLayoutSnapshot snapshot, string ownerNativeId, int left, int top, int width, int height)
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
                if (pair.Value.SourceNativeId != connection.SourceNativeId || pair.Value.SourcePortNativeId != connection.SourcePortNativeId ||
                    pair.Value.DestinationNativeId != connection.DestinationNativeId || pair.Value.DestinationPortNativeId != connection.DestinationPortNativeId ||
                    !pair.Value.RoutePoints.SequenceEqual(connection.RoutePoints))
                { differences.Add("designer.layout.connection_changed"); }
            }
            return new DesignerLayoutComparison(differences);
        }

        public DesignerLayoutComparison ComparePreservingExisting(DesignerLayoutSnapshot before,
            DesignerLayoutSnapshot after, IEnumerable<string> explicitlyMovedNodeKeys = null)
        {
            if (before == null) { throw new ArgumentNullException(nameof(before)); }
            if (after == null) { throw new ArgumentNullException(nameof(after)); }
            var allowedMoves = new HashSet<string>(explicitlyMovedNodeKeys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var afterNodes = after.Nodes.ToDictionary(node => node.Key, StringComparer.Ordinal);
            var differences = new List<string>();
            foreach (var node in before.Nodes)
            {
                if (!afterNodes.TryGetValue(node.Key, out var afterNode)) { differences.Add("designer.layout.existing_node_missing"); }
                else if (!allowedMoves.Contains(node.Key) && !Equals(node.Position, afterNode.Position))
                { differences.Add("designer.layout.existing_node_moved"); }
            }
            return new DesignerLayoutComparison(differences);
        }
    }
}