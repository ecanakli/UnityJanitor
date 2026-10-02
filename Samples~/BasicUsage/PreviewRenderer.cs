using Ecanakli.Janitor;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>Renders a camera into a temporary texture that lives while the object is active.</summary>
    public sealed class PreviewRenderer : MonoBehaviour
    {
        private Camera _camera;
        private RawImage _target;
        private RenderTexture _texture;

        // The views arrive here because the demo builds its UI in code.
        public void Initialize(Camera camera, RawImage target)
        {
            _camera = camera;
            _target = target;
        }

        // Takes the texture on first use and again after any end of the lifetime, so a cancel never leaves a released one.
        public void Render()
        {
            if (!gameObject.activeInHierarchy)
            {
                return;
            }

            if (_texture == null)
            {
                Acquire();
            }

            _camera.targetTexture = _texture;
            _camera.Render();
            _camera.targetTexture = null;
        }

        private void Acquire()
        {
            _texture = RenderTexture.GetTemporary(256, 256, 16);
            _target.texture = _texture;
            this.GetActiveLifetime().OnCancel(this, static self => self.Release());   // cleanup the package knows nothing about
        }

        private void Release()
        {
            RenderTexture.ReleaseTemporary(_texture);
            _texture = null;
            if (_target != null)
            {
                _target.texture = null;        // the target may already be destroyed
            }
        }
    }
}
