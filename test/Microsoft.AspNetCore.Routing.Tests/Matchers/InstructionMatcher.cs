// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Template;

namespace Microsoft.AspNetCore.Routing.Matchers
{
    internal class InstructionMatcher : Matcher
    {
        private State _state;

        public InstructionMatcher(Instruction[] instructions, Endpoint[] endpoints, JumpTable[] tables)
        {
            _state = new State()
            {
                Instructions = instructions,
                Endpoints = endpoints,
                Tables = tables,
            };
        }

        public unsafe override Task MatchAsync(HttpContext httpContext, IEndpointFeature feature)
        {
            if (httpContext == null)
            {
                throw new ArgumentNullException(nameof(httpContext));
            }

            if (feature == null)
            {
                throw new ArgumentNullException(nameof(feature));
            }

            var state = _state;

            var path = httpContext.Request.Path.Value;

            // This section tokenizes the path by marking the sequence of slashes, and their 
            // position in the string. The consuming code uses the sequence and the count of
            // slashes to deduce the length of each segment.
            //
            // If there is residue (text after last slash) then the length of the segment will
            // computed based on the string length.
            var buffer = stackalloc int[32];
            var count = 0;
            var index = 0; // PathString guarantees a leading / but we want to capture it.
            while ((index = path.IndexOf('/', index)) >= 0 && count < 32)
            {
                buffer[count++] = index++; // resume search after the current character
            }

            var i = 0;
            Endpoint result = null;
            while (i < state.Instructions.Length)
            {
                var instruction = state.Instructions[i];
                switch (instruction.Code)
                {
                    case InstructionCode.Accept:
                        {
                            result = state.Endpoints[instruction.Payload];
                            i++;
                            break;
                        }
                    case InstructionCode.Branch:
                        {
                            var table = state.Tables[instruction.Payload];
                            i = table.GetDestination(buffer, count, path);
                            break;
                        }
                    case InstructionCode.Jump:
                        {
                            i = instruction.Payload;
                            break;
                        }
                }
            }

            feature.Endpoint = result;
            feature.Values = new RouteValueDictionary();

            return Task.CompletedTask;
        }

        public class State
        {
            public Endpoint[] Endpoints;
            public Instruction[] Instructions;
            public JumpTable[] Tables;
        }

        [DebuggerDisplay("{ToDebugString(),nq}")]
        [StructLayout(LayoutKind.Explicit)]
        public struct Instruction
        {
            [FieldOffset(3)]
            public InstructionCode Code;

            [FieldOffset(4)]
            public int Payload;

            private string ToDebugString()
            {
                return $"{Code}: {Payload}";
            }
        }

        public enum InstructionCode : byte
        {
            Accept,
            Branch,
            Jump,
            Pop, // Only used during the instruction builder phase
        }

        public abstract class JumpTable
        {
            public unsafe abstract int GetDestination(int* segments, int count, string path);
        }

        public class JumpTableBuilder
        {
            private readonly List<(string text, int destination)> _entries = new List<(string text, int destination)>();

            public int Depth { get; set; }

            public int Exit { get; set; }

            public void AddEntry(string text, int destination)
            {
                _entries.Add((text, destination));
            }

            public JumpTable Build()
            {
                return new SimpleJumpTable(Depth, Exit, _entries.ToArray());
            }
        }

        public class SimpleJumpTable : JumpTable
        {
            private readonly (string text, int destination)[] _entries;
            private readonly int _depth;
            private readonly int _exit;

            public SimpleJumpTable(int depth, int exit, (string text, int destination)[] entries)
            {
                _depth = depth;
                _exit = exit;
                _entries = entries;
            }

            public unsafe override int GetDestination(int* segments, int count, string path)
            {
                if (_depth == count)
                {
                    return _exit;
                }

                var start  = segments[_depth] + 1;
                var length = count - _depth == 1 ? path.Length - start : segments[_depth + 1] - start;

                for (var i = 0; i < _entries.Length; i++)
                {
                    if (length == _entries[i].text.Length &&
                        string.Compare(
                        path,
                        start,
                        _entries[i].text,
                        0,
                        length,
                        StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        return _entries[i].destination;
                    }
                }

                return _exit;
            }
        }
    }
}
