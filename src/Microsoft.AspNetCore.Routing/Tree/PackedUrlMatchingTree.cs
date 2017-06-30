using Microsoft.AspNetCore.Routing.Internal;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Routing.Tree
{
    public class PackedUrlMatchingTree
    {
        private static readonly Task CompletedTask = Task.FromResult<object>(null);
        private PackedUrlMatchingTreeEntry[] _entries;
        private ILogger _constraintLogger;

        internal PackedUrlMatchingTree(PackedUrlMatchingTreeEntry[] entries, ILogger constraintLogger)
        {
            _entries = entries;
            _constraintLogger = constraintLogger;
        }

        public bool Route(RouteContext context, IRouter router)
        {
            // Try to match, starting from each top-level tree entry
            var nextEntryIndex = _entries.Length == 0 ? -1 : 0;
            while (nextEntryIndex != -1)
            {
                var pathTokenizer = new PathTokenizer(context.HttpContext.Request.Path);
                var topLevelEntry = _entries[nextEntryIndex];
                
                if (RouteRecursive(context, ref topLevelEntry, pathTokenizer.GetEnumerator(), router))
                {
                    return true;
                }

                nextEntryIndex = topLevelEntry.NextSiblingIndex;
            }
            
            return false;
        }

        private bool RouteRecursive(RouteContext context, ref PackedUrlMatchingTreeEntry rootEntry, PathTokenizer.Enumerator pathEnumerator, IRouter router)
        {
            // Can't match if there's nothing in the incoming URL to match against.
            // TODO: But what about optional params?
            if (!pathEnumerator.MoveNext())
            {
                return false;
            }

            var nextPathSegment = pathEnumerator.Current;

            // Bail out if we know this path segment can't match the tree node
            switch (rootEntry.Kind)
            {
                case EntryKind.Literal:
                    // Literal segments only match just based on a string comparison
                    if (!nextPathSegment.Equals(rootEntry.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    break;
            }

            // See whether we have a complete match by now
            var isLastPathSegment = nextPathSegment.Offset + nextPathSegment.Length == nextPathSegment.Buffer.Length;
            if (isLastPathSegment && rootEntry.Matches != null)
            {
                for (var matchIndex = 0; matchIndex < rootEntry.Matches.Length; matchIndex++)
                {
                    // Consider it a match if any of the route entries accept it
                    // (accounting for their constraints, etc.)
                    var match = rootEntry.Matches[matchIndex];
                    if (TryMatch(context, ref match, router))
                    {
                        return true;
                    }
                }

                return false;
            }

            // Not yet a complete match, nor sure that it won't match, so recurse into children
            var recurseIntoIndex = rootEntry.FirstChildIndex;
            while (recurseIntoIndex != -1)
            {
                if (RouteRecursive(context, ref _entries[recurseIntoIndex], pathEnumerator, router))
                {
                    return true;
                }

                recurseIntoIndex = _entries[recurseIntoIndex].NextSiblingIndex;
            }

            // There were no matching children, so we give up
            return false;
        }

        private bool TryMatch(RouteContext context, ref MatchEntry entry, IRouter router)
        {
            // Create a snapshot before processing the route. We'll restore this snapshot before running each
            // to restore the state. This is likely an "empty" snapshot, which doesn't allocate.
            var snapshot = context.RouteData.PushState(router: null, values: null, dataTokens: null);

            try
            {
                if (!entry.Matcher.TryMatch(context.HttpContext.Request.Path, context.RouteData.Values))
                {
                    return false;
                }

                if (!RouteConstraintMatcher.Match(
                    entry.Constraints,
                    context.RouteData.Values,
                    context.HttpContext,
                    router,
                    RouteDirection.IncomingRequest,
                    _constraintLogger))
                {
                    return false;
                }

                context.Handler = entry.Handler.GetRequestHandler(context.HttpContext, context.RouteData);
                return context.Handler != null;
            }
            finally
            {
                if (context.Handler == null)
                {
                    // Restore the original values to prevent polluting the route data.
                    snapshot.Restore();
                }
            }
        }

        internal enum EntryKind : byte
        {
            Literal,
            Parameter,
            Catchall,
        }

        internal struct PackedUrlMatchingTreeEntry
        {
            public EntryKind Kind;
            public int FirstChildIndex;
            public int NextSiblingIndex;
            public string Value;
            public MatchEntry[] Matches;
        }

        internal struct MatchEntry
        {
            public IDictionary<string, IRouteConstraint> Constraints;
            public TemplateMatcher Matcher;
            public IRouteHandler Handler;
        }
    }
}
