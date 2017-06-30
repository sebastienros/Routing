using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Routing.Template;
using static Microsoft.AspNetCore.Routing.Tree.PackedUrlMatchingTree;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Routing.Tree
{
    public class PackedUrlMatchingTreeBuilder
    {
        public static PackedUrlMatchingTree Build(IEnumerable<InboundRouteEntry> routeEntries, ILogger constraintLogger)
        {
            // First build a URL matching tree as a literal tree made of reference-type
            // objects in memory
            var topLevelNodes = new List<BuilderNode>();
            foreach (var routeEntry in routeEntries.OrderByDescending(e => e.Precedence))
            {
                InsertRouteEntryToBuilderNodeTree(routeEntry, 0, topLevelNodes);
            }

            // Now convert the URL matching tree to a more densely-packed array-of-structs
            // that we'll be able to walk much faster
            var treeEntries = ConvertToBreadthFirstEntryArray(topLevelNodes);
            return new PackedUrlMatchingTree(treeEntries, constraintLogger);
        }

        private static void InsertRouteEntryToBuilderNodeTree(InboundRouteEntry routeEntry, int startSegmentIndex, IList<BuilderNode> builderTreeLevelNodes)
        {
            var segment = routeEntry.RouteTemplate.Segments[startSegmentIndex];

            if (segment.IsSimple)
            {
                var segmentPart = segment.Parts.Single(); // "simple" means there's exactly one part
                var segmentPartText = segmentPart.Text;

                BuilderNode nextLevelParent;
                if (segmentPart.IsLiteral)
                {
                    nextLevelParent = GetOrCreateMatchingBuilderNode(
                        builderTreeLevelNodes,
                        node => node.Kind == EntryKind.Literal && string.Equals(segmentPartText, node.Value, StringComparison.OrdinalIgnoreCase),
                        () => new BuilderNode { Kind = EntryKind.Literal, Value = segmentPartText });
                }
                else if (segmentPart.IsParameter && !segmentPart.IsCatchAll)
                {
                    nextLevelParent = GetOrCreateMatchingBuilderNode(
                        builderTreeLevelNodes,
                        node => node.Kind == EntryKind.Parameter,
                        () => new BuilderNode { Kind = EntryKind.Parameter });
                }
                else
                {
                    throw new NotImplementedException("Other types of segments");
                }

                if (startSegmentIndex < routeEntry.RouteTemplate.Segments.Count - 1)
                {
                    InsertRouteEntryToBuilderNodeTree(routeEntry, startSegmentIndex + 1, nextLevelParent.Children);
                }
                else
                {
                    nextLevelParent.Matches.Add(new MatchEntry
                    {
                        Constraints = routeEntry.Constraints,
                        Handler = (IRouteHandler)routeEntry.Handler,
                        Matcher = new TemplateMatcher(routeEntry.RouteTemplate, routeEntry.Defaults),
                    });
                }
            }
            else
            {
                throw new NotImplementedException("Non-simple segment");
            }
        }

        private static BuilderNode GetOrCreateMatchingBuilderNode(IList<BuilderNode> existingNodes, Func<BuilderNode, bool> matcher, Func<BuilderNode> nodeCreator)
        {
            var existingNodeMatch = existingNodes.FirstOrDefault(matcher);
            if (existingNodeMatch != null)
            {
                return existingNodeMatch;
            }
            else
            {
                var newNode = nodeCreator();
                existingNodes.Add(newNode);
                return newNode;
            }
        }

        private static PackedUrlMatchingTreeEntry[] ConvertToBreadthFirstEntryArray(List<BuilderNode> topLevelNodes)
        {
            var result = new PackedUrlMatchingTreeEntry[CountTreeNodes(topLevelNodes)];

            VisitBreadthFirst(topLevelNodes, (node, index, parentIndex, isFirstSibling) =>
            {
                result[index] = new PackedUrlMatchingTreeEntry
                {
                    Kind = node.Kind,
                    Value = node.Value,
                    Matches = node.Matches.ToArray(),
                    FirstChildIndex = -1,
                    NextSiblingIndex = -1
                };

                // Link the previous sibling or parent to the new entry
                if (!isFirstSibling)
                {
                    result[index - 1].NextSiblingIndex = index;
                }
                else if (parentIndex >= 0)
                {
                    result[parentIndex].FirstChildIndex = index;
                }
            });

            return result;
        }

        private static int CountTreeNodes(IList<BuilderNode> topLevelNodes)
        {
            return topLevelNodes.Sum(node => 1 + CountTreeNodes(node.Children));
        }

        private static void VisitBreadthFirst(IList<BuilderNode> topLevelNodes, Action<BuilderNode, int, int, bool> visitorCallback)
        {
            var queueOfNodes = new Queue<BuilderNode>(topLevelNodes);
            var queueOfParentIndices = new Queue<int>(topLevelNodes.Select(x => -1));
            var currentNodeIndex = 0;
            var prevParentIndex = 0;
            while (queueOfNodes.Count > 0)
            {
                var currentNode = queueOfNodes.Dequeue();
                var currentNodeParentIndex = queueOfParentIndices.Dequeue();
                var isFirstSibling = currentNodeParentIndex != prevParentIndex;
                prevParentIndex = currentNodeParentIndex;

                visitorCallback(currentNode, currentNodeIndex, currentNodeParentIndex, isFirstSibling);

                foreach (var child in currentNode.Children)
                {
                    queueOfNodes.Enqueue(child);
                    queueOfParentIndices.Enqueue(currentNodeIndex);
                }

                currentNodeIndex++;
            }
        }
        
        private class BuilderNode
        {
            public int Index = -1;
            public EntryKind Kind;
            public List<BuilderNode> Children = new List<BuilderNode>();
            public List<MatchEntry> Matches = new List<MatchEntry>();
            public string Value;
        }
    }
}
