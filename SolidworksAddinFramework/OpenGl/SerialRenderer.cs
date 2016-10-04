using System;
using System.DoubleNumerics;
using System.Reactive.Subjects;
using LanguageExt;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace SolidworksAddinFramework.OpenGl
{
    /// <summary>
    /// A renderable which has a replaceable inner renderable
    /// </summary>
    public class SerialRenderer :ReactiveObject, IRenderer
    {
        private readonly Transformable _Transformable = new Transformable();
        private bool _Accumulate = false;

        IRenderer _Renderer = new EmptyRenderer();
        private readonly ISubject<Unit> _NeedsRedraw = new Subject<Unit>();

        public IRenderer Renderer 
        {
            get { return _Renderer; }
            set
            {
                Update(value);
                this.RaiseAndSetIfChanged(ref _Renderer, value);
                // ReSharper disable once ExplicitCallerInfoArgument
                this.RaisePropertyChanged(nameof(BoundingSphere));
                _NeedsRedraw.OnNext(Unit.Default);
            }
        }

        public IObservable<Unit> NeedsRedraw => _NeedsRedraw;

        public void Render(DateTime time)
        {
            Renderer.Render(time);
        }

        public void ApplyTransform(Matrix4x4 transform, bool accumulate = false)
        {
            _Transformable.ApplyTransform(transform, accumulate);
            Update(Renderer);
        }

        private void Update(IRenderer renderer)
        {
            renderer.ApplyTransform(_Transformable.Transform, false);
        }

        public Tuple<Vector3, double> BoundingSphere => Renderer.BoundingSphere;
    }
}