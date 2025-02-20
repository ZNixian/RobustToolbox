﻿using System;

namespace Robust.Shared.Maths
{
    /// <summary>
    ///     A representation of an angle, in radians.
    /// </summary>
    [Serializable]
    public readonly struct Angle : IApproxEquatable<Angle>, IEquatable<Angle>
    {
        public static Angle Zero { get; } = new Angle();
        public static Angle South { get; } = new Angle(-MathHelper.PiOver2);

        /// <summary>
        ///     Angle in radians.
        /// </summary>
        public readonly double Theta;

        /// <summary>
        ///     Angle in degrees.
        /// </summary>
        public double Degrees => MathHelper.RadiansToDegrees(Theta);

        /// <summary>
        ///     Constructs an instance of an Angle.
        /// </summary>
        /// <param name="theta">The angle in radians.</param>
        public Angle(double theta)
        {
            Theta = theta;
        }

        /// <summary>
        ///     Constructs an instance of an angle from an (un)normalized direction vector.
        /// </summary>
        /// <param name="dir"></param>
        public Angle(Vector2 dir)
        {
            dir = dir.Normalized;
            Theta = Math.Atan2(dir.Y, dir.X);
        }

        /// <summary>
        ///     Converts this angle to a unit direction vector.
        /// </summary>
        /// <returns>Unit Direction Vector</returns>
        public Vector2 ToVec()
        {
            var x = Math.Cos(Theta);
            var y = Math.Sin(Theta);
            return new Vector2((float) x, (float) y);
        }

        private const double Segment = 2 * Math.PI / 8.0; // Cut the circle into 8 pieces
        private const double Offset = Segment / 2.0; // offset the pieces by 1/2 their size

        public Direction GetDir()
        {
            var ang = Theta % (2 * Math.PI);

            if (ang < 0.0f) // convert -PI > PI to 0 > 2PI
                ang += 2 * (float) Math.PI;

            return (Direction) (Math.Floor((ang + Offset) / Segment) % 8);
        }

        private const double CardinalSegment = 2 * Math.PI / 4.0; // Cut the circle into 4 pieces
        private const double CardinalOffset = CardinalSegment / 2.0; // offset the pieces by 1/2 their size

        public Direction GetCardinalDir()
        {
            var ang = Theta % (2 * Math.PI);

            if (ang < 0.0f) // convert -PI > PI to 0 > 2PI
                ang += 2 * (float) Math.PI;

            return (Direction) (Math.Floor((ang + CardinalOffset) / CardinalSegment) * 2 % 8);
        }

        public bool EqualsApprox(Angle other, double tolerance)
        {
            return EqualsApprox(this, other, tolerance);
        }

        public bool EqualsApprox(Angle other)
        {
            return EqualsApprox(this, other);
        }

        private static bool EqualsApprox(Angle a, Angle b)
        {
            // reduce both angles
            var aReduced = Reduce(a.Theta);
            var bReduced = Reduce(b.Theta);

            var aPositive = FlipPositive(aReduced);
            var bPositive = FlipPositive(bReduced);

            // The second two expressions cover an edge case where one number is barely non-negative while the other number is negative.
            // In this case, the negative number will get FlipPositived to ~2pi and the comparison will give a false negative.
            return FloatMath.CloseTo(aPositive, bPositive)
                || FloatMath.CloseTo(aPositive + MathHelper.TwoPi, bPositive)
                || FloatMath.CloseTo(aPositive, bPositive + MathHelper.TwoPi);
        }

        private static bool EqualsApprox(Angle a, Angle b, double tolerance)
        {
            // reduce both angles
            var aReduced = Reduce(a.Theta);
            var bReduced = Reduce(b.Theta);

            var aPositive = FlipPositive(aReduced);
            var bPositive = FlipPositive(bReduced);

            // The second two expressions cover an edge case where one number is barely non-negative while the other number is negative.
            // In this case, the negative number will get FlipPositived to ~2pi and the comparison will give a false negative.
            return FloatMath.CloseTo(aPositive, bPositive, tolerance)
                || FloatMath.CloseTo(aPositive + MathHelper.TwoPi, bPositive, tolerance)
                || FloatMath.CloseTo(aPositive, bPositive + MathHelper.TwoPi, tolerance);
        }

        /// <summary>
        ///     Removes revolutions from a positive or negative angle to make it as small as possible.
        /// </summary>
        private static double Reduce(double theta)
        {
            // int truncates value (round to 0)
            var aTurns = (int) (theta / (2 * Math.PI));
            return theta - aTurns * (2 * Math.PI);
        }

        /// <inheritdoc />
        public bool Equals(Angle other)
        {
            return this.Theta.Equals(other.Theta);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is Angle angle && Equals(angle);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {

            return Theta.GetHashCode();

        }

        public static bool operator ==(Angle a, Angle b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(Angle a, Angle b)
        {
            return !(a == b);
        }

        /// <summary>
        ///     Calculates the congruent positive angle of a negative angle. Does nothing to a positive angle.
        /// </summary>
        private static double FlipPositive(double theta)
        {
            if (theta >= 0)
                return theta;

            return theta + 2 * Math.PI;
        }

        /// <summary>
        ///     Similar to Lerp but makes sure the angle wraps around to 360 degrees.
        /// </summary>
        public static Angle Lerp(in Angle a, in Angle b, float factor)
        {
            var degA = FloatMath.RadToDeg * Angle.Reduce(a);
            var degB = FloatMath.RadToDeg * Angle.Reduce(b);
            var delta = FloatMath.Repeat((float) (degB - degA), 360);
            if (delta > 180)
                delta -= 360;
            return new Angle(FloatMath.DegToRad * (degA + delta * FloatMath.Clamp01(factor)));
        }

        /// <summary>
        ///     Constructs a new angle, from degrees instead of radians.
        /// </summary>
        /// <param name="degrees">The angle in degrees.</param>
        public static Angle FromDegrees(double degrees)
        {
            return new Angle(MathHelper.DegreesToRadians(degrees));
        }

        /// <summary>
        ///     Implicit conversion from Angle to double.
        /// </summary>
        /// <param name="angle"></param>
        public static implicit operator double(Angle angle)
        {
            return angle.Theta;
        }

        /// <summary>
        ///     Implicit conversion from double to Angle.
        /// </summary>
        /// <param name="theta"></param>
        public static implicit operator Angle(double theta)
        {
            return new Angle(theta);
        }

        /// <summary>
        ///     Implicit conversion from float to Angle.
        /// </summary>
        /// <param name="theta"></param>
        public static implicit operator Angle(float theta)
        {
            return new Angle(theta);
        }

        public override string ToString()
        {
            return $"{Theta} rad";
        }
    }
}
