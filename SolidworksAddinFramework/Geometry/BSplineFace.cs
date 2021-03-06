using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.DoubleNumerics;
using LanguageExt;
using Newtonsoft.Json;

using SolidWorks.Interop.sldworks;
using Weingartner.WeinCad.Interfaces;
using Weingartner.WeinCad.Interfaces.Monads;
using static LanguageExt.Prelude;
using LogViewer = Weingartner.WeinCad.Interfaces.Wpf.LogViewer;

namespace SolidworksAddinFramework.Geometry
{
    [JsonObject(MemberSerialization.OptIn)]
    public class BSplineFace
    {
        [JsonProperty]
        public BSplineSurface Surface { get; }

        [JsonProperty]
        public IReadOnlyList<IReadOnlyList<BSpline2D>> TrimLoops { get; }

        public BSplineFace
            TransformSurfaceControlPoints(Func<Vector4[,], Vector4[,]> xformSurface)
        {
            var surface = Surface.WithCtrlPts(xformSurface);

            return new BSplineFace(surface, TrimLoops);
        }


        public BSplineFace(BSplineSurface surface, IReadOnlyList<IReadOnlyList<BSpline2D>> trimLoops)
        {
            Surface = surface;
            TrimLoops = trimLoops;
        }

        /// <summary>
        /// Create a BSplineFace from the TrimCurve data.
        /// http://help.solidworks.com/2015/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IFace2~GetTrimCurves2.html
        /// </summary>
        /// <param name="swFace"></param>
        /// <returns></returns>
        public static BSplineFace Create(IFace2 swFace )
        {
            var start = 0;

            var packedData = swFace.GetTrimCurves2(WantCubic: true, WantNRational: false).CastArray<double>();
            var reader = new GetTrimCurves2DataReader(packedData);

            // Packed Double 1
            // An integer pair containing number of loops and total number of SP curves (trim curves).
            // The length of any edge list generated immediately after a call to IFace2::GetTrimCurves2
            // will be equal to the number of SP curves
            var packedDouble1 = reader.ReadDouble().DoubleToInteger();
            int numLoops = packedDouble1.Item1;
            int numSPCurves = packedDouble1.Item2;

            // PackeDouble 2
            // Series of integer pairs containing the number of SP curves in each loop.
            // The first integer in each pair represents the number of curves in the odd loops;
            // the second represents the even. The total number of integer pairs is half the 
            // number of loops, rounded up
            var curvesPerLoopLookup = reader.ReadIntegers(numLoops).ToList();

            var i = 0;

            // PackedDouble 3[]  ( Array of doubles )
            // For each SP curve, a set of two integer pairs.
            // The first contains the order of the curve and
            // a Boolean indicating if it is periodic.If the curve is periodic,
            // it is clamped (that is, knots of multiplicity = order exists at each end of the curve).
            // The second contains the dimension of the curve and the number of control points in it. 
            // If the dimension is 2, then the curve is non - rational; if the dimension is 3,
            // then the curve is rational.
            var spCurveInfos = reader
                .ReadBufferedIntegers(bufferSize: 4, numberOfBuffers: numSPCurves)
                .Do(b =>
                {
                    LogViewer.Log($"Get TrimCurves2 'PackedDouble 3' buffer {i++}");
                    LogViewer.Log(string.Join(" ",b));
                })
                .Select(b => new {order = b[0], isPeriodic = b[1]== 1, dimension=b[2], isRational=b[2]==3, numCtrlPoints = b[3]})
                .ToList();

            var spCurveInfos2 = spCurveInfos
                .Select
                (info =>
                {
                    var knots = reader.Read(info.order + info.numCtrlPoints).ToList();
                    return new {info.order,info.isPeriodic, info.dimension, info.isRational, info.numCtrlPoints, knots};
                })
                .ToList();

            var trimCurves = spCurveInfos2
                .Select
                (info =>
                {
                    var ctrlPoints = reader
                        .Read(info.numCtrlPoints*info.dimension)
                        .Buffer(info.dimension, info.dimension)
                        .Select(ToRationalVector3)
                        .ToList();

                    return new BSpline2D
                        ( controlPoints: ctrlPoints.ToArray()
                        , knotVectorU: info.knots.ToArray()
                        , order: info.order
                        , isClosed: info.isPeriodic
                        , isRational: info.dimension==3);

                })
                .ToArray();


            var bLoops = curvesPerLoopLookup
                .Scan(new {start = 0, step = 0}, (acc, count) => new {start = acc.start + acc.step, step = count})
                .Skip(1)
                .Select(o => trimCurves.ToArraySegment(o.start, o.step).ToArray())
                .ToArray();


            fixLoops(bLoops);

            // packed double 4
            var surfaceDimension = reader.ReadDouble().DoubleToInteger().Item1;

            // packed double 5
            var uvOrder = reader.ReadDouble().DoubleToInteger().Map((u, v) => new {u, v});

            // packed double 6
            var uvNumCtrlPoints = reader.ReadDouble().DoubleToInteger().Map((u, v) => new {u, v});

            // packed double 7
            var uvIsPeriodic = reader.ReadDouble().DoubleToInteger().Map((u, v) => new {u, v});

            // surfaceKnotValuesU
            var uKnots = reader.Read(uvOrder.u + uvNumCtrlPoints.u).ToArray();
            
            // surfaceKnotValuesV
            var vKnots = reader.Read(uvOrder.v + uvNumCtrlPoints.v).ToArray();

            // surfaceCtrlPoinCoords
            var surfaceCtrlPoints = reader.Read(surfaceDimension*uvNumCtrlPoints.u*uvNumCtrlPoints.v)
                .Buffer(surfaceDimension, surfaceDimension)
                .Select(ToRationalVector4WithWeighRescaling)
                .ToList();

            // packed doubles 8 
            // TODO handle the case for multiple surfaces
            var indexFlags = reader.ReadDouble().DoubleToInteger().Map((nSurface, index) => new {nSurface, index});

            var ctrlPointsArray = surfaceCtrlPoints.Reshape(uvNumCtrlPoints.u, uvNumCtrlPoints.v);

            var bSurface = new BSplineSurface(ctrlPointsArray,uvOrder.u, uvOrder.v,uKnots, vKnots, surfaceDimension, uvIsPeriodic.u ==1, uvIsPeriodic.v==1);

            return new BSplineFace(bSurface, bLoops);

        }

        /// <summary>
        /// Make sure that the last point of each sub curve is exactly the same as the 
        /// first point of the next sub curve.
        /// </summary>
        /// <param name="trimLoops"></param>
        public static void fixLoops(IReadOnlyList<IReadOnlyList<BSpline2D>> trimLoops)
        {
            foreach (var loop in trimLoops)
            {
                var splines = loop.EndWith(loop.First());
                foreach (var pair in splines.Buffer(2,1).Where(b=>b.Count==2))
                {
                    // I assume that all subcurves are of the same dimension.
                    // but good to check.
                    Debug.Assert(pair[1].Dimension == pair[0].Dimension);
                    pair[1].ControlPoints[0] = pair[0].ControlPoints.Last();
                }
            }
        }

        /// <summary>
        /// Generates a solidworks sheet body representation of the bspline face.
        /// </summary>
        /// <returns>
        /// an optionof IBody2 which just makes it explicit that the
        /// caller needs to handle the case when the sheet body returned
        /// is null. 
        /// </returns>
        public Option<IBody2> ToSheetBody()
        {
            var surface = Surface.ToSurface();
            var loops = TrimLoops
                .SelectMany(loop => loop.Select(c => c.ToPCurve(surface)).EndWith(null))
                .SkipLast(1)
                .ToArray();

            var trimmedSheet4 = (IBody2) surface.CreateTrimmedSheet4
                ( Curves: loops
                , PreserveAnalyticCurves: true // We have tried 'false'. Doesn't make different to failures
                ); 

            return Optional(trimmedSheet4); // returns None if trimmedSheet4 is null
        }


        private static Vector3 ToRationalVector3(IList<double> data)
        {
            return new Vector3
                (data[0], data[1], data.Count == 3 ? data[2] : 1);
        }

        /// <summary>
        /// This transforms solidworks (X,Y,Z,W) to (X*W,Y*W,Z*W,W) format that is nurbs book standard
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private static Vector4 ToRationalVector4WithWeighRescaling(IList<double> data)
        {
            var w = data.Count == 4 ? data[3] : 1;

            return new Vector4
                (data[0] * w, data[1] * w, data[2] * w, w);
        }


    }
}