using System;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The exact sign of a 2×2 determinant over doubles, computed without extended
/// precision by sign normalization and an iterated remainder reduction whose every
/// step is exact in floating point (subtracting an integral multiple that stays in
/// range introduces no rounding). The substrate's point-in-ring crossing test rides
/// this: a crossing-side decision is a discrete parity answer whose error would be
/// unbounded, unlike the distance magnitudes that stay plain double.
/// </summary>
internal static class ExactSignDeterminant
{
    /// <summary>
    /// The sign of <c>x1·y2 − y1·x2</c>: −1, 0, or +1, exactly.
    /// </summary>
    public static int SignOfDeterminant(double x1, double y1, double x2, double y2)
    {
        int sign = 1;
        double swap;

        if(x1 == 0.0 || y2 == 0.0)
        {
            if(y1 == 0.0 || x2 == 0.0)
            {
                return 0;
            }

            if(y1 > 0)
            {
                return x2 > 0 ? -sign : sign;
            }

            return x2 > 0 ? sign : -sign;
        }

        if(y1 == 0.0 || x2 == 0.0)
        {
            if(y2 > 0)
            {
                return x1 > 0 ? sign : -sign;
            }

            return x1 > 0 ? -sign : sign;
        }

        //Make the y entries positive and order them so the second row has the larger y.
        if(0.0 < y1)
        {
            if(0.0 < y2)
            {
                if(y1 > y2)
                {
                    sign = -sign;
                    swap = x1;
                    x1 = x2;
                    x2 = swap;
                    swap = y1;
                    y1 = y2;
                    y2 = swap;
                }
            }
            else
            {
                if(y1 <= -y2)
                {
                    sign = -sign;
                    x2 = -x2;
                    y2 = -y2;
                }
                else
                {
                    swap = x1;
                    x1 = -x2;
                    x2 = swap;
                    swap = y1;
                    y1 = -y2;
                    y2 = swap;
                }
            }
        }
        else
        {
            if(0.0 < y2)
            {
                if(-y1 <= y2)
                {
                    sign = -sign;
                    x1 = -x1;
                    y1 = -y1;
                }
                else
                {
                    swap = -x1;
                    x1 = x2;
                    x2 = swap;
                    swap = -y1;
                    y1 = y2;
                    y2 = swap;
                }
            }
            else
            {
                if(y1 >= y2)
                {
                    x1 = -x1;
                    y1 = -y1;
                    x2 = -x2;
                    y2 = -y2;
                }
                else
                {
                    sign = -sign;
                    swap = -x1;
                    x1 = -x2;
                    x2 = swap;
                    swap = -y1;
                    y1 = -y2;
                    y2 = swap;
                }
            }
        }

        //Make the x entries positive; when the magnitudes already order the columns the
        //sign is decided.
        if(0.0 < x1)
        {
            if(0.0 < x2)
            {
                if(x1 > x2)
                {
                    return sign;
                }
            }
            else
            {
                return sign;
            }
        }
        else
        {
            if(0.0 < x2)
            {
                return -sign;
            }

            if(x1 >= x2)
            {
                sign = -sign;
                x1 = -x1;
                x2 = -x2;
            }
            else
            {
                return -sign;
            }
        }

        //All entries strictly positive with x1 <= x2 and y1 <= y2: reduce by exact
        //integral-multiple subtraction until a rectangle test decides the sign.
        while(true)
        {
            double k = Math.Floor(x2 / x1);
            x2 -= k * x1;
            y2 -= k * y1;

            if(y2 < 0.0)
            {
                return -sign;
            }

            if(y2 > y1)
            {
                return sign;
            }

            if(x1 > x2 + x2)
            {
                if(y1 < y2 + y2)
                {
                    return sign;
                }
            }
            else
            {
                if(y1 > y2 + y2)
                {
                    return -sign;
                }

                x2 = x1 - x2;
                y2 = y1 - y2;
                sign = -sign;
            }

            if(y2 == 0.0)
            {
                return x2 == 0.0 ? 0 : -sign;
            }

            if(x2 == 0.0)
            {
                return sign;
            }

            //Exchange the roles of the rows.
            k = Math.Floor(x1 / x2);
            x1 -= k * x2;
            y1 -= k * y2;

            if(y1 < 0.0)
            {
                return sign;
            }

            if(y1 > y2)
            {
                return -sign;
            }

            if(x2 > x1 + x1)
            {
                if(y2 < y1 + y1)
                {
                    return -sign;
                }
            }
            else
            {
                if(y2 > y1 + y1)
                {
                    return sign;
                }

                x1 = x2 - x1;
                y1 = y2 - y1;
                sign = -sign;
            }

            if(y1 == 0.0)
            {
                return x1 == 0.0 ? 0 : sign;
            }

            if(x1 == 0.0)
            {
                return -sign;
            }
        }
    }
}
