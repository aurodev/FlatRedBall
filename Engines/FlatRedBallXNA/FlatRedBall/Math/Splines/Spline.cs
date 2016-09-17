using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FlatRedBall.Math.Geometry;
using FlatRedBall.Utilities;

#if FRB_XNA || SILVERLIGHT || WINDOWS_PHONE

#if !XNA4 && !MONOGAME
using Color = Microsoft.Xna.Framework.Graphics.Color;
#endif
using System.Collections.ObjectModel;
using Microsoft.Xna.Framework;
#elif FRB_MDX
using Color = System.Drawing.Color;
using Vector3 = Microsoft.DirectX.Vector3;

#endif

namespace FlatRedBall.Math.Splines
{
    #region DistanceToTimeRelationship struct
    public struct DistanceToTimeRelationship
    {
        public double Distance;
        public double Time;

        public override string ToString()
        {
            return Distance.ToString() + " @ time " + Time;
        }
    }
    #endregion

    /// <summary>
    /// A curved line which can be used to move objects, direct AI, or create special effects.
    /// </summary>
    public class Spline : IList<SplinePoint>, INameable//, ICloneable
    {
        #region Fields


        List<SplinePoint> mSplinePoints = new List<SplinePoint>();

        bool mVisible = false;
        PositionedObjectList<Circle> mSplinePointsCircles = new PositionedObjectList<Circle>();
        PositionedObjectList<AxisAlignedRectangle> mPathRectangles = new PositionedObjectList<AxisAlignedRectangle>();

        List<DistanceToTimeRelationship> mDistanceToTimes = new List<DistanceToTimeRelationship>();

#if DEBUG
        private bool mAccelerationsCalculated;
#endif

        #endregion

        #region Properties

        /// <summary>
        /// The frequency (in time) to create shapes when Visible is set to true.
        /// </summary>
        public float PointFrequency
        {
            get;
            set;
        }

        /// <summary>
        /// The duration of the spline in time, calculated as the amount of time between the first spline point and the last.
        /// </summary>
        public double Duration
        {
            get
            {
                if (mSplinePoints.Count < 2)
                {
                    return 0;
                }
                else
                {
                    return mSplinePoints[mSplinePoints.Count - 1].Time -
                        mSplinePoints[0].Time;
                }
            }
        }

        /// <summary>
        /// The estimated length of the spline, as determined by calling CalculateDistanceTimeRelationships. This value
        /// is not available until CalculateDistanceTimeRelationships is called. CalculateDistanceTimeRelationships must be
        /// called if the spline changes or else this value may not be accurate.
        /// </summary>
        public double Length
        {
            get
            {

                if (mSplinePoints.Count < 2)
                {
                    return 0;
                }
                else
                {
#if DEBUG
                    if (mDistanceToTimes == null || mDistanceToTimes.Count == 0)
                    {
                        throw new InvalidOperationException("CalculateDistanceTimeRelationships() must be called before getting a Spline's Length.");
                    }
#endif
                    return mDistanceToTimes[mDistanceToTimes.Count - 1].Distance;
                }
            }
        }

        public string Name
        {
            get;
            set;
        }

        public Color PathColor
        {
            get;
            set;
        }

        public Color PointColor
        {
            get;
            set;
        }

        public float SplinePointVisibleRadius
        {
            get { return SplinePointRadiusInPixels / SpriteManager.Camera.PixelsPerUnitAt(0); }
        }

        public float SplinePointRadiusInPixels
        {
            get;
            set;
        }


        public double StartTime
        {
            get
            {
                if (mSplinePoints.Count == 0)
                {
                    return 0;
                }
                else
                {
                    return mSplinePoints[0].Time;
                }
            }
        }

        public bool Visible
        {
            get { return mVisible; }
            set
            {
                if (mVisible != value)
                {
                    mVisible = value;
                    UpdateShapes();
                }
            }
        }

        #endregion

        #region Methods

        #region Constructor

        /// <summary>
        /// Constructs a Spline with no points. Points can be added by calling the Add method.
        /// </summary>
        public Spline()
        {
            PointColor = Color.White;
            PathColor = Color.Red;

            PointFrequency = .2f;
            SplinePointRadiusInPixels = 8;
        }

        /// <summary>
        /// Creates a spline given a set of points and a time to take. The time assigned to each point is evenly spaced.
        /// The first point will have a Time of 0, the last will have a time of timeToTake.
        /// </summary>
        /// <param name="points">The points, evenly distributed over timeToTake.</param>
        /// <param name="timeToTake">The duration in time of the entire spline.</param>
        public Spline(IList<Vector3> points, double timeToTake)
        {
            SplinePointRadiusInPixels = 8;
            PointFrequency = .2f;

            PointColor = Color.White;
            PathColor = Color.Red;

            double pointCountMinusOneAsDouble = (double)(points.Count - 1);

            for (int i = 0; i < points.Count; i++)
            {
                SplinePoint splinePoint = new SplinePoint();

                splinePoint.Position = points[i];

                splinePoint.Time = i * timeToTake / pointCountMinusOneAsDouble;

                mSplinePoints.Add(splinePoint);

            }

            CalculateVelocities();

            CalculateAccelerations();
        }

        #endregion

        #region Public Methods

        public void CalculateAccelerations()
        {
            for (int i = 0; i < mSplinePoints.Count - 1; i++)
            {
                SplinePoint start = mSplinePoints[i];
                SplinePoint end = mSplinePoints[i + 1];

                double timeBetweenPoints = end.Time - start.Time;

                Vector3 midpointVelocity = (2 / (float)timeBetweenPoints) * (end.Position - start.Position) -
                    .5f * (start.Velocity + end.Velocity);

                start.Acceleration = (midpointVelocity - start.Velocity) * (1 / (float)(timeBetweenPoints * .5f));
            }

#if DEBUG
            mAccelerationsCalculated = true;
#endif
        }


        public void CalculateVelocities()
        {
            for (int i = 1; i < mSplinePoints.Count - 1; i++)
            {
                if (mSplinePoints[i].UseCustomVelocityValue == false)
                {
                    // It's not the first or last, so set the velocities
                    float timeFromBeforeAndAfter = (float)(mSplinePoints[i + 1].Time - mSplinePoints[i - 1].Time);

                    mSplinePoints[i].Velocity =
                        (mSplinePoints[i + 1].Position - mSplinePoints[i - 1].Position) * (1 / timeFromBeforeAndAfter);
                }
            }
        }

        public void CalculateDistanceTimeRelationships(float timeInterval)
        {
            if (this.Count == 0)
            {
                return;
            }
#if DEBUG
            if (!mAccelerationsCalculated)
            {
                throw new InvalidOperationException("Spline.CalculateAccelerations() must be called before Spline.CalculateDistanceTimeRelationships()");
            }
#endif
            float currentTime = 0;
            Vector3 lastPosition = this[0].Position;
            Vector3 currentPosition;

            float runningDistance = 0;

            mDistanceToTimes.Clear();

            DistanceToTimeRelationship start = new DistanceToTimeRelationship();
            mDistanceToTimes.Add(start);

            while (currentTime < this.Duration)
            {
                currentTime += timeInterval;

                DistanceToTimeRelationship dttr = new DistanceToTimeRelationship();
                dttr.Time = currentTime;

                currentPosition = GetPositionAtTime(currentTime);
                runningDistance += (currentPosition - lastPosition).Length();
                System.Diagnostics.Debug.WriteLine(String.Format("Time: {0}\tDistance: {1}", currentTime, runningDistance));
                dttr.Distance = runningDistance;

                mDistanceToTimes.Add(dttr);

                lastPosition = currentPosition;
            }
        }

        public Vector3 GetPositionAtLengthAlongSpline(float length)
        {
            double time = GetTimeAtLengthAlongSpline(length);

            return GetPositionAtTime(time);
        }

        public double GetTimeAtLengthAlongSpline(float length)
        {
#if DEBUG
            if (mDistanceToTimes == null || mDistanceToTimes.Count == 0)
            {
                throw new InvalidOperationException("CalculateDistanceTimeRelationships() must be called before calling GetTimeAtLengthAlongSpline.");
            }
#endif

            if (length < 0)
                length = 0;

            int indexBeforeTime = GetDistanceTimeRelationshipIndexBeforeLength(length);

            if (length == 0)
            {
                return indexBeforeTime = 0;
            }
            if (indexBeforeTime == mDistanceToTimes.Count - 1)
            {
                return mDistanceToTimes[indexBeforeTime].Time;
            }
            else
            {
                int indexAfterTime = indexBeforeTime + 1;

                double distanceAfterLastRelationship = length - mDistanceToTimes[indexBeforeTime].Distance;
                double distanceBetweenRelationships = mDistanceToTimes[indexAfterTime].Distance - mDistanceToTimes[indexBeforeTime].Distance;

                double ratio = distanceAfterLastRelationship / distanceBetweenRelationships;

                return mDistanceToTimes[indexBeforeTime].Time + (ratio * (mDistanceToTimes[indexAfterTime].Time - mDistanceToTimes[indexBeforeTime].Time));
            }
        }

        public double GetLengthAtTime(double time)
        {
#if DEBUG
            if (mDistanceToTimes == null || mDistanceToTimes.Count == 0)
            {
                throw new InvalidOperationException("CalculateDistanceTimeRelationships() must be called before calling GetLengthAtTime.");
            }
#endif

            if (time < 0)
                time = 0;

            int indexBeforeTime = GetDistanceTimeRelationshipIndexBeforeTime(time);

            if (time == 0)
            {
                return indexBeforeTime = 0;
            }
            if (indexBeforeTime == mDistanceToTimes.Count - 1)
            {
                return mDistanceToTimes[indexBeforeTime].Distance;
            }
            else
            {
                int indexAfterTime = indexBeforeTime + 1;

                double timeAfterLastRelationship = time - mDistanceToTimes[indexBeforeTime].Time;
                double timeBetweenRelationships = mDistanceToTimes[indexAfterTime].Time - mDistanceToTimes[indexBeforeTime].Time;

                double ratio = timeAfterLastRelationship / timeBetweenRelationships;

                return mDistanceToTimes[indexBeforeTime].Distance + (ratio * (mDistanceToTimes[indexAfterTime].Distance - mDistanceToTimes[indexBeforeTime].Distance));
            }
        }

        public Vector3 GetPositionAtTime(double time)
        {
            SplinePoint before;
            SplinePoint after;
            return GetPositionAtTime(time, out before, out after);
        }

        private Vector3 GetPositionAtTime(double time, out SplinePoint before, out SplinePoint after)
        {
            before = null;
            after = null;
            #region Special-case for a count of 0 or 1
            if (mSplinePoints.Count == 0)
                return new Vector3();

            if (mSplinePoints.Count == 1)
            {
                return mSplinePoints[0].Position;
            }
            #endregion

            #region First see if there are any SplinePoints exactly at this time
            for (int i = 0; i < mSplinePoints.Count; i++)
            {
                SplinePoint splinePoint = mSplinePoints[i];

                if (splinePoint.Time == time)
                {
                    return splinePoint.Position;
                }
                else if(splinePoint.Time > time)
                {
                    break;
                }

            }

            #endregion

            #region If we get here that means we're between SplinePoints

            before = GetSplinePointBefore(time);
            after = GetSplinePointAfter(time);

            Vector3 toReturn = GetPositionBetweenSplinePoints(before, after, time);


            #endregion

            return toReturn;
        }


        private Vector3 GetPositionBetweenSplinePoints(SplinePoint start, SplinePoint end, double time)
        {
            Vector3 toReturn;
            if (end == null)
            {
                // There's nothing after this time, so just return the end position
                toReturn = mSplinePoints[mSplinePoints.Count - 1].Position;
            }

            else if (start == null)
            {
                // there's nothing before this time, so just return the start position
                toReturn = mSplinePoints[0].Position;
            }
            else
            {

                // If we get here that means that there are at least 2 SplinePoints in this Spline
                // and that the time is between these two points.  We can do our average calculations
                // to see where the points lie.
                double timeBetweenPoints = end.Time - start.Time;

                Vector3 midpointVelocity = (2 / (float)timeBetweenPoints) * (end.Position - start.Position) -
                    (start.Velocity + end.Velocity) * (1 / 2.0f);

                double midpointTime = start.Time + timeBetweenPoints / 2.0;

                if (time < midpointTime)
                {

                    toReturn = MathFunctions.GetPositionAfterTime(
                        ref start.Position, ref start.Velocity, ref start.Acceleration, time - start.Time);

                }
                else
                {
                    Vector3 midPosition = MathFunctions.GetPositionAfterTime(
                        ref start.Position, ref start.Velocity, ref start.Acceleration, midpointTime - start.Time);

                    Vector3 midpointAcceleration = (end.Velocity - midpointVelocity) * (1 / (float)(timeBetweenPoints * .5f));

                    toReturn = MathFunctions.GetPositionAfterTime(
                        ref midPosition, ref midpointVelocity, ref midpointAcceleration,
                        time - midpointTime);
                }
            }

            return toReturn;
        }

        public Vector3 GetVectorAtTime(double time)
        {
            #region Special-case for a count of 0 or 1
            if (mSplinePoints.Count == 0)
                return new Vector3();

            if (mSplinePoints.Count == 1)
            {
                return mSplinePoints[0].Velocity;
            }
            #endregion

            #region First see if there are any SplinePoints exactly at this time
            for (int i = 0; i < mSplinePoints.Count; i++)
            {
                SplinePoint splinePoint = mSplinePoints[i];

                if (splinePoint.Time == time)
                {
                    return splinePoint.Velocity;
                }
            }

            #endregion

            #region If we get here that means we're between SplinePoints

            SplinePoint start = GetSplinePointBefore(time);
            SplinePoint end = GetSplinePointAfter(time);

            if (end == null)
            {
                // There's nothing after this time, so just return the end position
                return mSplinePoints[mSplinePoints.Count - 1].Velocity;
            }

            if (start == null)
            {
                // there's nothing before this time, so just return the start position
                return mSplinePoints[0].Velocity;
            }

            // If we get here that means that there are at least 2 SplinePoints in this Spline
            // and that the time is between these two points.  We can do our average calculations
            // to see where the points lie.
            double timeBetweenPoints = end.Time - start.Time;

            Vector3 midpointVelocity = (2 / (float)timeBetweenPoints) * (end.Position - start.Position) -
                (start.Velocity + end.Velocity) * (1 / 2.0f);

            double midpointTime = start.Time + timeBetweenPoints / 2.0;

            if (time < midpointTime)
            {
                return start.Velocity + start.Acceleration * (float)(time - start.Time);
            }
            else
            {
                Vector3 midpointAcceleration = (end.Velocity - midpointVelocity) * (1 / (float)(timeBetweenPoints * .5f));

                return midpointVelocity + midpointAcceleration * (float)(time - midpointTime);
            }

            #endregion

        }

        public SplinePoint GetSplinePointAfter(double time)
        {
            for (int i = 0; i < mSplinePoints.Count; i++)
            {
                SplinePoint splinePoint = mSplinePoints[i];

                if (splinePoint.Time > time)
                {
                    return splinePoint;
                }
            }

            return null;
        }

        public SplinePoint GetSplinePointBefore(double time)
        {
            for (int i = mSplinePoints.Count - 1; i > -1; i--)
            {
                SplinePoint splinePoint = mSplinePoints[i];

                if (splinePoint.Time < time)
                {
                    return splinePoint;
                }
            }

            return null;
        }

        private DistanceToTimeRelationship GetDttrClosestToPointAlongSpline(Vector3 testPoint)
        {
            if (mDistanceToTimes == null || mDistanceToTimes.Count == 0)
            {
#if DEBUG
                throw new InvalidOperationException("CalculateDistanceTimeRelationships() must be called before getting the closest point on a spline.");
#endif
                return new DistanceToTimeRelationship();
            }

            float shortestDistSquared = float.MaxValue;
            int shortestIndex = -1;

            SplinePoint before = null;
            SplinePoint after = null;


            Vector3 positionAtTime = Vector3.Zero;
            bool handled = false;
            bool isInRange = false;
            float currentDistSquared;
            DistanceToTimeRelationship currentItem;

            var count = mDistanceToTimes.Count;

            for (int i = 0; i < count; ++i)
            {
                currentItem = mDistanceToTimes[i];
                handled = false;
                if (before != null && after != null)
                {
                    isInRange = currentItem.Time > before.Time && currentItem.Time < after.Time;

                    if(isInRange)
                    {
                        positionAtTime = GetPositionBetweenSplinePoints(before, after, currentItem.Time);
                        handled = true;
                    }
                }

                if (!handled)
                {
                    positionAtTime = GetPositionAtTime(currentItem.Time, out before, out after);
                }

                currentDistSquared = Vector3.DistanceSquared(testPoint, positionAtTime);

                if (currentDistSquared < shortestDistSquared)
                {
                    shortestDistSquared = currentDistSquared;
                    shortestIndex = i;
                }
            }

            return mDistanceToTimes[shortestIndex];
        }

        public double GetDistanceClosestToPointAlongSpline(Vector3 testPoint)
        {
            return GetDttrClosestToPointAlongSpline(testPoint).Distance;
        }

        public double GetTimeClosestToPointAlongSpline(Vector3 testPoint)
        {
            return GetDttrClosestToPointAlongSpline(testPoint).Time;
        }

        public void Shift(float shiftX, float shiftY, float shiftZ)
        {
            foreach (SplinePoint sp in this)
            {
                sp.Position += new Vector3(shiftX, shiftY, shiftZ);
            }
        }

        public void Sort()
        {
            if (mSplinePoints.Count == 1 || mSplinePoints.Count == 0)
                return;

            int whereObjectBelongs;

            for (int i = 1; i < mSplinePoints.Count; i++)
            {
                if (mSplinePoints[i].Time < mSplinePoints[i - 1].Time)
                {
                    if (i == 1)
                    {
                        mSplinePoints.Insert(0, this[i]);
                        mSplinePoints.RemoveAt(i + 1);
                        continue;
                    }

                    for (whereObjectBelongs = i - 2; whereObjectBelongs > -1; whereObjectBelongs--)
                    {
                        if (mSplinePoints[i].Time >= mSplinePoints[whereObjectBelongs].Time)
                        {
                            mSplinePoints.Insert(whereObjectBelongs + 1, this[i]);
                            mSplinePoints.RemoveAt(i + 1);
                            break;
                        }
                        else if (whereObjectBelongs == 0 && mSplinePoints[i].Time < mSplinePoints[0].Time)
                        {
                            mSplinePoints.Insert(0, this[i]);
                            mSplinePoints.RemoveAt(i + 1);
                            break;
                        }
                    }
                }
            }
        }

        public void UpdateShapes()
        {
            #region If Invisible, remove everything
            if (mVisible == false)
            {
                while (mSplinePointsCircles.Count != 0)
                {
                    ShapeManager.Remove(mSplinePointsCircles.Last);
                }

                while (mPathRectangles.Count != 0)
                {
                    ShapeManager.Remove(mPathRectangles.Last);
                }
            }
            #endregion

            else
            {
                float radius = SplinePointVisibleRadius;

                #region Create enough SplinePoint Circles for the Spline


                while (mSplinePoints.Count > mSplinePointsCircles.Count)
                {
                    Circle newCircle = ShapeManager.AddCircle();
                    mSplinePointsCircles.Add(newCircle);
                }

                #endregion

                #region Remove any extra SplinePoints

                while (mSplinePoints.Count < mSplinePointsCircles.Count)
                {
                    ShapeManager.Remove(mSplinePointsCircles.Last);
                }

                #endregion

                double duration = Duration;
                int numberOfRectangles = 1 + (int)(duration / PointFrequency);

                #region Create enough Path Rectangles for the Spline

                while (numberOfRectangles > mPathRectangles.Count)
                {
                    AxisAlignedRectangle aar = ShapeManager.AddAxisAlignedRectangle();
                    mPathRectangles.Add(aar);
                }

                #endregion

                #region Remove any extra Path Rectangles

                while (numberOfRectangles < mPathRectangles.Count)
                {
                    ShapeManager.Remove(mPathRectangles.Last);
                }

                #endregion

                #region Update the SplinePoint Circle Positions and Colors

                for (int i = 0; i < mSplinePoints.Count; i++)
                {
                    mSplinePointsCircles[i].Position = mSplinePoints[i].Position;
                    mSplinePointsCircles[i].Color = PointColor;
                    mSplinePointsCircles[i].Radius = radius;

                }

                #endregion

                #region Update the Path Rectangle Positions and Colors

                double fractionOfTime = this.Duration / (double)mPathRectangles.Count;

                double startTime = 0;

                if (mSplinePoints.Count != 0)
                {
                    startTime = mSplinePoints[0].Time;
                }

                for (int i = 0; i < mPathRectangles.Count; i++)
                {
                    mPathRectangles[i].Position = GetPositionAtTime(startTime + i * fractionOfTime);
                    mPathRectangles[i].Color = PathColor;
                    mPathRectangles[i].ScaleX = mPathRectangles[i].ScaleY = radius / 2.0f;
                }

                #endregion

            }
        }

        #endregion

        #region Private Methods

        private int GetDistanceTimeRelationshipIndexBeforeLength(double length)
        {
            for (int i = mDistanceToTimes.Count - 1; i > -1; i--)
            {
                if (mDistanceToTimes[i].Distance < length)
                {
                    return i;
                }
            }

            return -1;

        }

        private int GetDistanceTimeRelationshipIndexBeforeTime(double time)
        {
            for (int i = mDistanceToTimes.Count - 1; i > -1; i--)
            {
                if (mDistanceToTimes[i].Time < time)
                {
                    return i;
                }
            }

            return -1;

        }

        #endregion

        #region Overridden Interface Methods
        #region IList<SplinePoint> Members

        public int IndexOf(SplinePoint item)
        {
            return mSplinePoints.IndexOf(item);
        }

        public void Insert(int index, SplinePoint item)
        {
            throw new NotSupportedException("Cannot insert because SplinePoints must be sorted");
        }

        public void RemoveAt(int index)
        {
            mSplinePoints.RemoveAt(index);
        }

        public SplinePoint this[int index]
        {
            get
            {
                return mSplinePoints[index];
            }
            set
            {
                mSplinePoints[index] = value;
            }
        }

        #endregion

        #region ICollection<SplinePoint> Members

        public void Add(SplinePoint item)
        {
            mSplinePoints.Add(item);
            Sort();
        }

        public void Clear()
        {
            mSplinePoints.Clear();
        }

        public bool Contains(SplinePoint item)
        {
            return mSplinePoints.Contains(item);
        }

        public void CopyTo(SplinePoint[] array, int arrayIndex)
        {
            mSplinePoints.CopyTo(array, arrayIndex);
        }

        public int Count
        {
            get { return mSplinePoints.Count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        public bool Remove(SplinePoint item)
        {
            return ((ICollection<SplinePoint>)mSplinePoints).Remove(item);
        }

        #endregion

        #region IEnumerable<SplinePoint> Members

        public IEnumerator<SplinePoint> GetEnumerator()
        {
            return mSplinePoints.GetEnumerator();
        }

        #endregion

        #region IEnumerable Members

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        #endregion

        #region ICloneable Members

        public Spline Clone()
        {
            Spline clone = new Spline();

            foreach (SplinePoint splinePoint in mSplinePoints)
            {
                clone.Add(splinePoint.Clone() as SplinePoint);
            }

            clone.Visible = this.Visible;

            return clone as Spline;
        }

        #endregion

        #endregion

    }
}
