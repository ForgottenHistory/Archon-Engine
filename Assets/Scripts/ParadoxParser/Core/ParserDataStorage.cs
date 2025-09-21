using System;
using Unity.Collections;
using Unity.Jobs;
using ParadoxParser.Data;

namespace ParadoxParser.Core
{
    public unsafe struct ParserDataStorage : IDisposable
    {
        private NativeList<ParseNode> m_ParseNodes;
        private StringInternSystem m_StringPool;
        private NativeDynamicArray<int> m_ChildArrays;
        private NativeHashMap<int, int> m_NodeLookup;

        public bool IsCreated => m_ParseNodes.IsCreated && m_StringPool.IsCreated;

        public ParserDataStorage(int initialCapacity, Allocator allocator)
        {
            m_ParseNodes = new NativeList<ParseNode>(initialCapacity, allocator);
            m_StringPool = new StringInternSystem(initialCapacity / 4, allocator);
            m_ChildArrays = new NativeDynamicArray<int>(initialCapacity / 8, allocator);
            m_NodeLookup = new NativeHashMap<int, int>(initialCapacity, allocator);

            CommonStrings.PreIntern(ref m_StringPool);
        }

        public int AddNode(NodeType type, string value = null, int parentIndex = -1)
        {
            var stringId = value != null ? m_StringPool.InternString(value) : -1;
            var node = new ParseNode(type, stringId, parentIndex);

            var nodeIndex = m_ParseNodes.Length;
            m_ParseNodes.Add(node);

            if (stringId >= 0)
            {
                m_NodeLookup[stringId] = nodeIndex;
            }

            return nodeIndex;
        }

        public void SetNodeChildren(int nodeIndex, NativeArray<int> childIndices)
        {
            if (nodeIndex < 0 || nodeIndex >= m_ParseNodes.Length)
                return;

            var node = m_ParseNodes[nodeIndex];
            node.ChildStartIndex = m_ChildArrays.AddArray(childIndices);
            node.ChildCount = childIndices.Length;
            m_ParseNodes[nodeIndex] = node;
        }

        public ParseNode GetNode(int nodeIndex)
        {
            if (nodeIndex < 0 || nodeIndex >= m_ParseNodes.Length)
                return default;

            return m_ParseNodes[nodeIndex];
        }

        public string GetNodeValue(int nodeIndex)
        {
            var node = GetNode(nodeIndex);
            if (node.StringId < 0)
                return null;

            var fixedStr = m_StringPool.GetString(node.StringId);
            return fixedStr.ToString();
        }

        public NativeArray<int> GetNodeChildren(int nodeIndex, Allocator allocator)
        {
            var node = GetNode(nodeIndex);
            if (node.ChildStartIndex < 0)
                return new NativeArray<int>(0, allocator);

            return m_ChildArrays.GetArray(node.ChildStartIndex, allocator);
        }

        public bool TryFindNodeByValue(string value, out int nodeIndex)
        {
            nodeIndex = -1;

            if (!m_StringPool.TryGetStringId(new FixedString128Bytes(value), out int stringId))
                return false;

            return m_NodeLookup.TryGetValue(stringId, out nodeIndex);
        }

        public int NodeCount => m_ParseNodes.Length;
        public int StringCount => m_StringPool.Count;

        public void Dispose()
        {
            if (m_ParseNodes.IsCreated)
                m_ParseNodes.Dispose();
            if (m_StringPool.IsCreated)
                m_StringPool.Dispose();
            if (m_ChildArrays.IsCreated)
                m_ChildArrays.Dispose();
            if (m_NodeLookup.IsCreated)
                m_NodeLookup.Dispose();
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            var job1 = m_ParseNodes.Dispose(inputDeps);
            var job2 = m_ChildArrays.Dispose(job1);
            var job3 = m_NodeLookup.Dispose(job2);
            return job3;
        }
    }
}