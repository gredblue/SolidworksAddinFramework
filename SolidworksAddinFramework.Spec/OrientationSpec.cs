﻿using System;
using System.DoubleNumerics;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using SolidworksAddinFramework.Events;
using SolidWorks.Interop.sldworks;
using XUnit.Solidworks.Addin;

namespace SolidworksAddinFramework.Spec
{
    public class OrientationSpec : SolidWorksSpec
    {
        [SolidworksFact]
        public async Task OrientationShouldWork()
        {
            await CreatePartDoc(async doc =>
            {
                var view = (IModelView) doc.ActiveView;
                var math = SwAddinBase.Active.Math;
                var matrix = Matrix4x4.CreateLookAt(Vector3.UnitZ, Vector3.Zero, Vector3.UnitY);
                view.Orientation3 = math.ToSwMatrix(matrix);
                await Task.WhenAny(doc.ClearSelectionsNotifyObservable().FirstAsync().ToTask(), Task.Delay(TimeSpan.FromSeconds(30)));
                matrix = matrix*Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, (double)Math.PI/4);
                view.Orientation3 = math.ToSwMatrix(matrix);
                await Task.WhenAny(doc.ClearSelectionsNotifyObservable().FirstAsync().ToTask(), Task.Delay(TimeSpan.FromSeconds(30)));
            });
        }
    }
}
