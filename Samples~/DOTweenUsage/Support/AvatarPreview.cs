using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>Stand-in for the game's avatar preview: a colored square with a name.</summary>
    public sealed class AvatarPreview
    {
        private readonly Image _body;
        private readonly TMP_Text _label;

        public AvatarPreview(Image body, TMP_Text label)
        {
            _body = body;
            _label = label;
        }

        public Transform Root => _body.transform;

        public void Show(OutfitLook look)
        {
            _body.transform.localScale = Vector3.one;
            _body.color = look.Color;
            _label.text = look.Name;
        }
    }
}
