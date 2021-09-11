﻿using FlatRedBall.Utilities;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Text;

// not built in to .NET until .net 5...
//using System.Text.Json;

namespace FlatRedBall.Math.Paths
{
    #region Enums

    public enum SegmentType
    {
        Line,
        Arc
    }

    public enum AngleUnit
    {
        Degrees,
        Radians
    }

    #endregion

    #region PathSegment Class
    public class PathSegment
    {
        public SegmentType SegmentType;

        public bool IsRelative;
        public float StartX;
        public float StartY;

        Vector2 Start => new Vector2(StartX, StartY);

        public float EndX;
        public float EndY;

        Vector2 End => new Vector2(EndX, EndY);

        public float ArcAngle;
        public Vector2 CircleCenter;

        public float CalculatedLength;

        public AngleUnit AngleUnit;

        public Vector2 PointAtLength(float lengthFromStart)
        {
            if(SegmentType == SegmentType.Line)
            {
                return Start + (End - Start).AtLength(lengthFromStart);
            }
            else
            {
                var centerToStart = Start - CircleCenter;

                var radius = centerToStart.Length();

                var anglePerArcLength = 1 / radius;

                var angleToRotateBy = System.Math.Sign(ArcAngle) * anglePerArcLength * lengthFromStart;

                return CircleCenter + centerToStart.RotatedBy(angleToRotateBy);
            }
        }

    }

    #endregion

    public class Path : INameable
    {
        #region Fields/Properties

        List<PathSegment> Segments { get; set; } = new List<PathSegment>();

        float currentX;
        float currentY;

        public float TotalLength { get; private set; }
        public string Name { get; set; }

        #endregion

        public void Clear()
        {
            Segments.Clear();
            TotalLength = 0;
            currentX = 0;
            currentY = 0;
        }

        public void MoveTo(float x, float y)
        {
            currentX = x;
            currentY = y;
        }

        public void MoveToRelative(float x, float y)
        {
            currentX += x;
            currentY += y;
        }

        public void LineTo(float x, float y)
        {
            var pathSegment = GetSegmentToAbsolutePoint(x, y, SegmentType.Line);
            var xDifference = pathSegment.EndX - pathSegment.StartX;
            var yDifference = pathSegment.EndY - pathSegment.StartY;

            pathSegment.CalculatedLength = (float)System.Math.Sqrt(xDifference * xDifference + yDifference * yDifference);
            Segments.Add(pathSegment);
            TotalLength += pathSegment.CalculatedLength;
        }
        public void LineToRelative(float x, float y)
        {
            var pathSegment = GetSegmentToAbsolutePoint(currentX + x, currentY + y, SegmentType.Line);
            var xDifference = pathSegment.EndX - pathSegment.StartX;
            var yDifference = pathSegment.EndY - pathSegment.StartY;

            pathSegment.CalculatedLength = (float)System.Math.Sqrt(xDifference * xDifference + yDifference * yDifference);
            Segments.Add(pathSegment);
            TotalLength += pathSegment.CalculatedLength;

        }

        public void ArcTo(float endX, float endY, float signedAngle)
        {
            var pathSegment = GetSegmentToAbsolutePoint(endX, endY, SegmentType.Arc);
            pathSegment.ArcAngle = signedAngle;
            AssignArcLength(pathSegment);
            Segments.Add(pathSegment);
            TotalLength += pathSegment.CalculatedLength;

        }
        public void ArcToRelative(float endX, float endY, float signedAngle)
        {
            var pathSegment = GetSegmentToAbsolutePoint(currentX + endX, currentY + endY, SegmentType.Arc);
            pathSegment.ArcAngle = signedAngle;

            AssignArcLength(pathSegment);
            Segments.Add(pathSegment);
            TotalLength += pathSegment.CalculatedLength;

        }

        void AssignArcLength(PathSegment segment)
        {
            var first = new Vector2(segment.StartX, segment.StartY);
            var second = new Vector2(segment.EndX, segment.EndY);

            var firstToSecond = second - first;

            var firstTangent = firstToSecond.RotatedBy(-segment.ArcAngle / 2);
            var secondTangent = firstToSecond.RotatedBy(segment.ArcAngle / 2);

            // normal of (x,y) is (y, -x)
            var firstNormal = new Vector2(firstTangent.Y, -firstTangent.X);
            var secondNormal = new Vector2(secondTangent.Y, -secondTangent.X);


            // from here:
            // https://stackoverflow.com/questions/4543506/algorithm-for-intersection-of-2-lines
            // Update, this had a bug and I coulnd't figure it out because I don't understand
            // the math well enough to debug it. Moving to a different solution:

            var firstSegment = new Geometry.Segment(segment.StartX, segment.StartY,
                segment.StartX + firstNormal.X, segment.StartY + firstNormal.Y);

            var secondSegment = new Geometry.Segment(segment.EndX, segment.EndY,
                segment.EndX + secondNormal.X, segment.EndY + secondNormal.Y);

            var intersection = FindIntersection(firstSegment, secondSegment);


            //void GetABC(Geometry.Segment segmentInner, out double A, out double B, out double C)
            //{

            //    // using A = y2-y1; B = x1-x2; C = Ax1+By1
            //    A = segmentInner.Point2.Y - segmentInner.Point1.Y;
            //    B = segmentInner.Point1.X - segmentInner.Point2.X;
            //    C = A * segmentInner.Point1.X + B * segmentInner.Point1.Y;

            //}

            //GetABC(firstSegment, out double A1, out double B1, out double C1);
            //GetABC(secondSegment, out double A2, out double B2, out double C2);

            //var delta = A1 * B2 - A2 * B1;

            float radius;

            if (intersection == null)
            {
                radius = (first - second).Length()/2.0f;
                segment.CircleCenter = (first + second) / 2.0f;
            }
            else
            {
                segment.CircleCenter = intersection.Value;

                radius = (first - segment.CircleCenter).Length();
            }

            segment.CalculatedLength = System.Math.Abs(radius * segment.ArcAngle);

        }

        //  Returns Point of intersection if do intersect otherwise default Point (null)
        static Vector2? FindIntersection(Geometry.Segment lineA, Geometry.Segment lineB, double tolerance = 0.001)
        {
            double x1 = lineA.Point1.X, y1 = lineA.Point1.Y;
            double x2 = lineA.Point2.X, y2 = lineA.Point2.Y;

            double x3 = lineB.Point1.X, y3 = lineB.Point1.Y;
            double x4 = lineB.Point2.X, y4 = lineB.Point2.Y;

            // equations of the form x = c (two vertical lines)
            if (System.Math.Abs(x1 - x2) < tolerance && System.Math.Abs(x3 - x4) < tolerance && System.Math.Abs(x1 - x3) < tolerance)
            {
                return null;
            }

            //equations of the form y=c (two horizontal lines)
            if (System.Math.Abs(y1 - y2) < tolerance && System.Math.Abs(y3 - y4) < tolerance && System.Math.Abs(y1 - y3) < tolerance)
            {
                return null;
            }

            //equations of the form x=c (two vertical parallel lines)
            if (System.Math.Abs(x1 - x2) < tolerance && System.Math.Abs(x3 - x4) < tolerance)
            {
                return null;
            }

            //equations of the form y=c (two horizontal parallel lines)
            if (System.Math.Abs(y1 - y2) < tolerance && System.Math.Abs(y3 - y4) < tolerance)
            {
                return null;
            }

            //general equation of line is y = mx + c where m is the slope
            //assume equation of line 1 as y1 = m1x1 + c1 
            //=> -m1x1 + y1 = c1 ----(1)
            //assume equation of line 2 as y2 = m2x2 + c2
            //=> -m2x2 + y2 = c2 -----(2)
            //if line 1 and 2 intersect then x1=x2=x & y1=y2=y where (x,y) is the intersection point
            //so we will get below two equations 
            //-m1x + y = c1 --------(3)
            //-m2x + y = c2 --------(4)

            double x, y;

            //lineA is vertical x1 = x2
            //slope will be infinity
            //so lets derive another solution
            if (System.Math.Abs(x1 - x2) < tolerance)
            {
                //compute slope of line 2 (m2) and c2
                double m2 = (y4 - y3) / (x4 - x3);
                double c2 = -m2 * x3 + y3;

                //equation of vertical line is x = c
                //if line 1 and 2 intersect then x1=c1=x
                //subsitute x=x1 in (4) => -m2x1 + y = c2
                // => y = c2 + m2x1 
                x = x1;
                y = c2 + m2 * x1;
            }
            //lineB is vertical x3 = x4
            //slope will be infinity
            //so lets derive another solution
            else if (System.Math.Abs(x3 - x4) < tolerance)
            {
                //compute slope of line 1 (m1) and c2
                double m1 = (y2 - y1) / (x2 - x1);
                double c1 = -m1 * x1 + y1;

                //equation of vertical line is x = c
                //if line 1 and 2 intersect then x3=c3=x
                //subsitute x=x3 in (3) => -m1x3 + y = c1
                // => y = c1 + m1x3 
                x = x3;
                y = c1 + m1 * x3;
            }
            //lineA & lineB are not vertical 
            //(could be horizontal we can handle it with slope = 0)
            else
            {
                //compute slope of line 1 (m1) and c2
                double m1 = (y2 - y1) / (x2 - x1);
                double c1 = -m1 * x1 + y1;

                //compute slope of line 2 (m2) and c2
                double m2 = (y4 - y3) / (x4 - x3);
                double c2 = -m2 * x3 + y3;

                //solving equations (3) & (4) => x = (c1-c2)/(m2-m1)
                //plugging x value in equation (4) => y = c2 + m2 * x
                x = (c1 - c2) / (m2 - m1);
                y = c2 + m2 * x;

                //verify by plugging intersection point (x, y)
                //in orginal equations (1) & (2) to see if they intersect
                //otherwise x,y values will not be finite and will fail this check
                if (!(System.Math.Abs(-m1 * x + y - c1) < tolerance
                    && System.Math.Abs(-m2 * x + y - c2) < tolerance))
                {
                    //return default (no intersection)
                    return null;
                }
            }

            ////x,y can intersect outside the line segment since line is infinitely long
            ////so finally check if x, y is within both the line segments
            //if (IsInsideLine(lineA, x, y) &&
            //    IsInsideLine(lineB, x, y))
            //{
            return new Vector2((float)x, (float)y);
            //}

        }

        PathSegment GetSegmentToAbsolutePoint(float absoluteX, float absoluteY, SegmentType segmentType)
        {
            var pathSegment = new PathSegment();

            pathSegment.SegmentType = segmentType;

            pathSegment.StartX = currentX;
            pathSegment.StartY = currentY;

            pathSegment.EndX = absoluteX;
            pathSegment.EndY = absoluteY;

            currentX = pathSegment.EndX;
            currentY = pathSegment.EndY;

            return pathSegment;
        }

        public Vector2 PointAtLength(float length)
        {
            var lengthSoFar = 0f;
            var spilloverLength = 0f;
            PathSegment segmentToUse = null;

            if (length >= TotalLength && Segments.Count > 0)
            {
                var segment = Segments[Segments.Count - 1];
                return segment.PointAtLength(segment.CalculatedLength);
            }
            else
            {
                for(int i = 0; i < Segments.Count; i++)
                {
                    var segmentLength = Segments[i].CalculatedLength;
                    if(lengthSoFar + segmentLength > length)
                    {
                        segmentToUse = Segments[i];
                        spilloverLength = length - lengthSoFar;
                        break;
                    }
                    else
                    {
                        lengthSoFar += segmentLength;
                    }
                }

                if(segmentToUse != null)
                {
                    return segmentToUse.PointAtLength(spilloverLength);
                }
                else
                {
                    return Vector2.Zero; 
                }
            }

        }

        public Vector2 PointAtSegmentIndex(int index)
        {
            var segment = Segments[index];
            return new Vector2(segment.StartX, segment.StartY);
        }

        public float LengthAtSegmentIndex(int index)
        {
            var lengthSoFar = 0f;

            for(int i = 0; i < index; i++)
            {
                lengthSoFar += Segments[i].CalculatedLength;
            }

            return lengthSoFar;
        }

        public void FromJson(string serializedSegments)
        {
            var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<List<PathSegment>>(serializedSegments);
            Clear();
            foreach (var item in deserialized)
            {
                if (item.SegmentType == SegmentType.Line)
                {
                    LineToRelative(item.EndX, item.EndY);
                }
                else if (item.SegmentType == SegmentType.Arc)
                {
                    var angle = item.ArcAngle;
                    if(item.AngleUnit == AngleUnit.Degrees)
                    {
                        angle = Microsoft.Xna.Framework.MathHelper.ToRadians(angle);
                    }
                    ArcToRelative(item.EndX, item.EndY, angle);
                }
                else
                {
                    // Unknown segment type...
                }
            }
        }
    }
}
