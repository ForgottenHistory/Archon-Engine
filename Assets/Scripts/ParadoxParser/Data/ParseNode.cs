using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ParadoxParser.Data
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ParseNode : IEquatable<ParseNode>
    {
        public NodeType Type;
        public int StringId;
        public int ParentIndex;
        public int ChildStartIndex;
        public int ChildCount;
        public int LineNumber;
        public int ColumnNumber;

        public ParseNode(NodeType type, int stringId = -1, int parentIndex = -1)
        {
            Type = type;
            StringId = stringId;
            ParentIndex = parentIndex;
            ChildStartIndex = -1;
            ChildCount = 0;
            LineNumber = 0;
            ColumnNumber = 0;
        }

        public bool Equals(ParseNode other)
        {
            return Type == other.Type &&
                   StringId == other.StringId &&
                   ParentIndex == other.ParentIndex &&
                   ChildStartIndex == other.ChildStartIndex &&
                   ChildCount == other.ChildCount;
        }

        public override bool Equals(object obj)
        {
            return obj is ParseNode other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Type, StringId, ParentIndex, ChildStartIndex, ChildCount);
        }

        public bool IsValid => Type != NodeType.Invalid;
        public bool HasChildren => ChildCount > 0;
        public bool IsRoot => ParentIndex == -1;
    }

    public enum NodeType : byte
    {
        Invalid = 0,
        Root,
        Identifier,
        String,
        Number,
        Boolean,
        Date,
        Block,
        Assignment,
        List,
        Comment
    }
}