using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace SolidworksAddinFramework.OpenGl.Animation
{
    public sealed class MultiAnimator : AnimatorBase
    {
        private readonly Func<TimeSpan, double> _GetCurrentValue;
        private readonly IReadOnlyList<AnimatorBase> _Animators;
        private DateTime _ReferenceTime;

        public override TimeSpan Duration => _Animators.Aggregate(TimeSpan.Zero, (sum, a) => sum + a.Duration);
        public override IReadOnlyList<IAnimationSection> Sections => _Animators.SelectMany(a => a.Sections).ToList();

        public MultiAnimator([NotNull] IEnumerable<AnimatorBase> animators, Func<TimeSpan, double> getCurrentValue = null)
        {
            if (animators == null) throw new ArgumentNullException(nameof(animators));
            _GetCurrentValue = getCurrentValue ??
                (t => (t.TotalMilliseconds % Duration.TotalMilliseconds) / Duration.TotalMilliseconds);

            _Animators = animators.ToList();
        }

        public override void CalculateSectionTimes(DateTime startTime)
        {
            if (_Animators.Count == 0) return;

            _ReferenceTime = startTime;
            var animatorStartTime = _ReferenceTime;
            foreach (var animator in _Animators)
            {
                animator.CalculateSectionTimes(animatorStartTime);
                animatorStartTime += animator.Duration;
            }
        }

        public override void Render(DateTime now)
        {
            if (_Animators.Count == 0) return;

            var value = _GetCurrentValue(now - _ReferenceTime);
            var time = TimeSpan.FromMilliseconds(Duration.TotalMilliseconds * value);

            FindAnimator(time).Render(_ReferenceTime + time);
        }

        private AnimatorBase FindAnimator(TimeSpan time)
        {
            var duration = TimeSpan.Zero;
            foreach (var animator in _Animators)
            {
                duration += animator.Duration;
                if (duration >= time)
                {
                    return animator;
                }
            }
            throw new IndexOutOfRangeException("Can't find animator.");
        }
    }
}