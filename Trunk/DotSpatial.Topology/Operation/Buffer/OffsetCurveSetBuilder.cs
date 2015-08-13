// ********************************************************************************************************
// Product Name: DotSpatial.Topology.dll
// Description:  The basic topology module for the new dotSpatial libraries
// ********************************************************************************************************
// The contents of this file are subject to the Lesser GNU Public License (LGPL)
// you may not use this file except in compliance with the License. You may obtain a copy of the License at
// http://dotspatial.codeplex.com/license  Alternately, you can access an earlier version of this content from
// the Net Topology Suite, which is also protected by the GNU Lesser Public License and the sourcecode
// for the Net Topology Suite can be obtained here: http://sourceforge.net/projects/nts.
//
// Software distributed under the License is distributed on an "AS IS" basis, WITHOUT WARRANTY OF
// ANY KIND, either expressed or implied. See the License for the specific language governing rights and
// limitations under the License.
//
// The Original Code is from the Net Topology Suite, which is a C# port of the Java Topology Suite.
//
// The Initial Developer to integrate this code into MapWindow 6.0 is Ted Dunsford.
//
// Contributor(s): (Open source contributors should list themselves and their modifications here).
// |         Name         |    Date    |                              Comment
// |----------------------|------------|------------------------------------------------------------
// |                      |            |
// ********************************************************************************************************

using System;
using System.Collections.Generic;
using DotSpatial.Topology.Algorithm;
using DotSpatial.Topology.Geometries;
using DotSpatial.Topology.GeometriesGraph;
using DotSpatial.Topology.Noding;

namespace DotSpatial.Topology.Operation.Buffer
{
    /// <summary>
    /// Creates all the raw offset curves for a buffer of a <c>Geometry</c>.
    /// Raw curves need to be noded together and polygonized to form the final buffer area.
    /// </summary>
    public class OffsetCurveSetBuilder
    {
        #region Fields

        private readonly OffsetCurveBuilder _curveBuilder;
        private readonly IList<ISegmentString> _curveList = new List<ISegmentString>();
        private readonly double _distance;
        private readonly IGeometry _inputGeom;

        #endregion

        #region Constructors

        /// <summary>
        ///
        /// </summary>
        /// <param name="inputGeom"></param>
        /// <param name="distance"></param>
        /// <param name="curveBuilder"></param>
        public OffsetCurveSetBuilder(IGeometry inputGeom, double distance, OffsetCurveBuilder curveBuilder)
        {
            _inputGeom = inputGeom;
            _distance = distance;
            _curveBuilder = curveBuilder;
        }

        #endregion

        #region Methods

        /// <summary>
        ///
        /// </summary>
        /// <param name="g"></param>
        private void Add(IGeometry g)
        {
            if (g.IsEmpty) return;
            if (g is IPolygon)
                AddPolygon(g);
            // LineString also handles LinearRings
            else if (g is ILineString)
                AddLineString(g);
            else if (g is IPoint)
                AddPoint(g);
            else if (g is IMultiPoint)
                AddCollection(g);
            else if (g is IMultiLineString)
                AddCollection(g);
            else if (g is IMultiPolygon)
                AddCollection(g);
            else if (g is IGeometryCollection)
                AddCollection(g);
            else throw new NotSupportedException(g.GetType().FullName);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="gc"></param>
        private void AddCollection(IGeometry gc)
        {
            for (int i = 0; i < gc.NumGeometries; i++)
            {
                IGeometry g = gc.GetGeometryN(i);
                Add(g);
            }
        }

        /// <summary>
        /// Creates a {SegmentString} for a coordinate list which is a raw offset curve,
        /// and adds it to the list of buffer curves.
        /// The SegmentString is tagged with a Label giving the topology of the curve.
        /// The curve may be oriented in either direction.
        /// If the curve is oriented CW, the locations will be:
        /// Left: Location.Exterior.
        /// Right: LocationType.Interior.
        /// </summary>
        private void AddCurve(IList<Coordinate> coord, LocationType leftLoc, LocationType rightLoc)
        {
            // don't add null or trivial curves!
            if (coord == null || coord.Count < 2) return;
            // add the edge for a coordinate list which is a raw offset curve
            var e = new NodedSegmentString(coord, new Label(0, LocationType.Boundary, leftLoc, rightLoc));
            _curveList.Add(e);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="line"></param>
        private void AddLineString(IGeometry line)
        {
            // a zero or negative width buffer of a line/point is empty
            if (_distance <= 0.0 && !_curveBuilder.BufferParameters.IsSingleSided)
                return;
            var coord = CoordinateArrays.RemoveRepeatedPoints(line.Coordinates);
            var curve = _curveBuilder.GetLineCurve(coord, _distance);
            AddCurve(curve, LocationType.Exterior, LocationType.Interior);
        }

        /// <summary>
        /// Add a Point to the graph.
        /// </summary>
        /// <param name="p"></param>
        private void AddPoint(IGeometry p)
        {
            // a zero or negative width buffer of a line/point is empty
            if (_distance <= 0.0)
                return;
            var coord = p.Coordinates;
            var curve = _curveBuilder.GetLineCurve(coord, _distance);
            AddCurve(curve, LocationType.Exterior, LocationType.Interior);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="p"></param>
        private void AddPolygon(IPolygon p)
        {
            double offsetDistance = _distance;
            PositionType offsetSide = PositionType.Left;
            if (_distance < 0.0)
            {
                offsetDistance = -_distance;
                offsetSide = PositionType.Right;
            }

            ILinearRing shell = p.Shell;
            IList<Coordinate> shellCoord = CoordinateArrays.RemoveRepeatedPoints(shell.Coordinates);
            // optimization - don't bother computing buffer
            // if the polygon would be completely eroded
            if (_distance < 0.0 && IsErodedCompletely(shellCoord, _distance)) return;
            // don't attemtp to buffer a polygon with too few distinct vertices
            if (_distance <= 0.0 && shellCoord.Count < 3) return;

            AddPolygonRing(shellCoord, offsetDistance, offsetSide, LocationType.Exterior, LocationType.Interior);

            for (int i = 0; i < p.NumHoles; i++)
            {
                ILinearRing hole = (ILinearRing)p.GetInteriorRingN(i);
                IList<Coordinate> holeCoord = CoordinateArrays.RemoveRepeatedPoints(hole.Coordinates);

                // optimization - don't bother computing buffer for this hole
                // if the hole would be completely covered
                if (_distance > 0.0 && IsErodedCompletely(holeCoord, -_distance)) continue;

                // Holes are topologically labelled opposite to the shell, since
                // the interior of the polygon lies on their opposite side
                // (on the left, if the hole is oriented CCW)
                AddPolygonRing(holeCoord, offsetDistance, Position.Opposite(offsetSide), LocationType.Interior, LocationType.Exterior);
            }
        }

        /// <summary>
        /// Adds an offset curve for a polygon ring.
        /// The side and left and right topological location arguments
        /// assume that the ring is oriented CW.
        /// If the ring is in the opposite orientation,
        /// the left and right locations must be interchanged and the side flipped.
        /// </summary>
        /// <param name="coord">The coordinates of the ring (must not contain repeated points).</param>
        /// <param name="offsetDistance">The distance at which to create the buffer.</param>
        /// <param name="side">The side of the ring on which to construct the buffer line.</param>
        /// <param name="cwLeftLoc">The location on the L side of the ring (if it is CW).</param>
        /// <param name="cwRightLoc">The location on the R side of the ring (if it is CW).</param>
        private void AddPolygonRing(IList<Coordinate> coord, double offsetDistance, PositionType side, LocationType cwLeftLoc, LocationType cwRightLoc)
        {
            // don't bother adding ring if it is "flat" and will disappear in the output
            if (offsetDistance == 0.0 && coord.Count < LinearRing.MinimumValidSize)
                return;
            LocationType leftLoc = cwLeftLoc;
            LocationType rightLoc = cwRightLoc;
            if (coord.Count >= LinearRing.MinimumValidSize && CgAlgorithms.IsCounterClockwise(coord))
            {
                leftLoc = cwRightLoc;
                rightLoc = cwLeftLoc;
                side = Position.Opposite(side);
            }
            var curve = _curveBuilder.GetRingCurve(coord, side, offsetDistance);
            AddCurve(curve, leftLoc, rightLoc);
        }

        /// <summary>
        /// Computes the set of raw offset curves for the buffer.
        /// Each offset curve has an attached {Label} indicating
        /// its left and right location.
        /// </summary>
        /// <returns>A Collection of SegmentStrings representing the raw buffer curves.</returns>
        public virtual IList<ISegmentString> GetCurves()
        {
            Add(_inputGeom);
            return _curveList;
        }

        /// <summary>
        /// The ringCoord is assumed to contain no repeated points.
        /// It may be degenerate (i.e. contain only 1, 2, or 3 points).
        /// In this case it has no area, and hence has a minimum diameter of 0.
        /// </summary>
        /// <param name="ringCoord"></param>
        /// <param name="bufferDistance"></param>
        /// <returns></returns>
        private bool IsErodedCompletely(IList<Coordinate> ringCoord, double bufferDistance)
        {
            // degenerate ring has no area
            if (ringCoord.Count < 4)
                return bufferDistance < 0;

            // important test to eliminate inverted triangle bug
            // also optimizes erosion test for triangles
            if (ringCoord.Count == 4)
                return IsTriangleErodedCompletely(ringCoord, bufferDistance);

            /*
             * The following is a heuristic test to determine whether an
             * inside buffer will be eroded completely.
             * It is based on the fact that the minimum diameter of the ring pointset
             * provides an upper bound on the buffer distance which would erode the
             * ring.
             * If the buffer distance is less than the minimum diameter, the ring
             * may still be eroded, but this will be determined by
             * a full topological computation.
             *
             */
            ILinearRing ring = _inputGeom.Factory.CreateLinearRing(ringCoord);
            MinimumDiameter md = new MinimumDiameter(ring);
            return md.Length < 2 * Math.Abs(bufferDistance);
        }

        /// <summary>
        /// Tests whether a triangular ring would be eroded completely by the given
        /// buffer distance.
        /// This is a precise test.  It uses the fact that the inner buffer of a
        /// triangle converges on the inCentre of the triangle (the point
        /// equidistant from all sides).  If the buffer distance is greater than the
        /// distance of the inCentre from a side, the triangle will be eroded completely.
        /// This test is important, since it removes a problematic case where
        /// the buffer distance is slightly larger than the inCentre distance.
        /// In this case the triangle buffer curve "inverts" with incorrect topology,
        /// producing an incorrect hole in the buffer.
        /// </summary>
        /// <param name="triangleCoord"></param>
        /// <param name="bufferDistance"></param>
        /// <returns></returns>
        private bool IsTriangleErodedCompletely(IList<Coordinate> triangleCoord, double bufferDistance)
        {
            Triangle tri = new Triangle(triangleCoord[0], triangleCoord[1], triangleCoord[2]);
            Coordinate inCentre = tri.InCentre();
            double distToCentre = CgAlgorithms.DistancePointLine(inCentre, tri.P0, tri.P1);
            return distToCentre < Math.Abs(bufferDistance);
        }

        #endregion
    }
}