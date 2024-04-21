using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnitySlangShader
{
    [Serializable]
    public struct SlangShaderVariant : IEquatable<SlangShaderVariant>
    {
        // TODO: Canonicalize
        [SerializeField]
        public string[] Keywords;

        public SlangShaderVariant(string[] keywords)
        {
            Keywords = keywords;
        }

        public override int GetHashCode()
        {
            return ((IStructuralEquatable)Keywords).GetHashCode(EqualityComparer<string>.Default);
        }

        public bool Equals(SlangShaderVariant other)
        {
            if (other.Keywords.Length != Keywords.Length)
                return false;

            for (int i = 0; i < Keywords.Length; i++)
            {
                if (Keywords[i] != other.Keywords[i])
                    return false;
            }

            return true;
        }

        public override string ToString() => $"SlangShaderVariant({string.Join(", ", Keywords)})";
        public override bool Equals(object obj) => obj is SlangShaderVariant v && Equals(v);
    }
}