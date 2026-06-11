using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FightOrFlight.UI
{
    /// <summary>
    /// Lightweight hover/press feedback for UI buttons: a small smooth scale
    /// pop on pointer-over and a press dip. Drop it on any button — no per-frame
    /// cost when idle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ButtonHoverEffect : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private float hoverScale = 1.06f;
        [SerializeField] private float pressScale = 0.96f;
        [SerializeField] private float speed = 12f;

        private RectTransform _rect;
        private Vector3 _baseScale;
        private Vector3 _target;
        private Coroutine _animation;
        private bool _hovering;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _baseScale = _rect.localScale;
            _target = _baseScale;
        }

        private void OnDisable()
        {
            // Reset so a disabled/re-enabled button never gets stuck mid-pop.
            if (_rect != null)
                _rect.localScale = _baseScale;
            _target = _baseScale;
            _hovering = false;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovering = true;
            AnimateTo(_baseScale * hoverScale);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovering = false;
            AnimateTo(_baseScale);
        }

        public void OnPointerDown(PointerEventData eventData) => AnimateTo(_baseScale * pressScale);

        public void OnPointerUp(PointerEventData eventData) => AnimateTo(_hovering ? _baseScale * hoverScale : _baseScale);

        private void AnimateTo(Vector3 target)
        {
            _target = target;
            if (_animation == null)
                _animation = StartCoroutine(Animate());
        }

        private IEnumerator Animate()
        {
            while ((_rect.localScale - _target).sqrMagnitude > 0.0000001f)
            {
                _rect.localScale = Vector3.Lerp(_rect.localScale, _target, Time.unscaledDeltaTime * speed);
                yield return null;
            }
            _rect.localScale = _target;
            _animation = null;
        }
    }
}
