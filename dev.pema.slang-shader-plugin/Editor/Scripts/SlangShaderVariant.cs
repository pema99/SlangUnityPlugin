using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnitySlangShader
{
    [Serializable]
    public struct SlangShaderVariant : IEquatable<SlangShaderVariant>
    {
        [SerializeField]
        private string[] SerializedKeywords;

        private HashSet<string> cachedKeywords;
        public HashSet<string> Keywords => cachedKeywords ??= (SerializedKeywords ??= Array.Empty<string>()).ToHashSet();

        public SlangShaderVariant(HashSet<string> keywords)
        {
            SerializedKeywords = keywords.ToArray();
            cachedKeywords = keywords;
        }

        public override int GetHashCode()
        {
            return HashSet<string>.CreateSetComparer().GetHashCode(Keywords);
        }

        public bool Equals(SlangShaderVariant other)
        {
            return Keywords.SetEquals(other.SerializedKeywords);
        }

        public override string ToString() => $"SlangShaderVariant({string.Join(", ", Keywords)})";
        public override bool Equals(object obj) => obj is SlangShaderVariant v && Equals(v);
    }
}