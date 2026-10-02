using UnityEngine;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>What the preview shows for an outfit.</summary>
    public readonly struct OutfitLook
    {
        public OutfitLook(string name, Color color)
        {
            Name = name;
            Color = color;
        }

        public string Name { get; }
        public Color Color { get; }
    }
}
