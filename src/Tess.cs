// The Direct3D 11 fixed-function tessellator, triangle domain only, as the game's ground shader uses it (Custom/Heightmap
// with tessellation on: partitioning fractional_odd, output triangle_cw). Ported line for line from Microsoft's reference
// "D3D11 Fixed Function Tessellator Reference" (CHWTessellator, tessellator.cpp/.hpp, May 2012), published in
// github.com/microsoft/DirectX-Specs under the MIT License, Copyright (c) Microsoft Corporation. Hardware must place the
// points exactly as the reference does, so knowing the tessellation factors gives the exact points and triangles the GPU
// draws. Checked against the C++ reference on many factor sets (tools/tesscheck).
// Only what the triangle domain with odd fractional partitioning needs is kept; names follow the reference.
using System;

namespace PotatoPainter
{
    internal sealed class Tess
    {
        private const int FXP_FRACTION_BITS = 16;
        private const uint FXP_FRACTION_MASK = 0x0000ffff, FXP_INTEGER_MASK = 0x7fff0000, FXP_ONE = 1u << FXP_FRACTION_BITS;
        private const uint FXP_ONE_THIRD = 0x00005555, FXP_TWO_THIRDS = 0x0000aaaa, FXP_ONE_HALF = 0x00008000;
        private const float EPSILON = 0.0000152587890625f;   // 2^-16
        private const float MIN_ODD = 1f, MAX_ODD = 63f;
        private const int Ueq0 = 0, Veq0 = 1, Weq0 = 2, TRI_EDGES = 3;

        private static readonly uint[] s_fixedReciprocal =
        {
            0xffffffff,
            0x10000, 0x8000, 0x5555, 0x4000, 0x3333, 0x2aab, 0x2492, 0x2000, 0x1c72, 0x199a, 0x1746, 0x1555, 0x13b1, 0x1249, 0x1111, 0x1000,
            0xf0f, 0xe39, 0xd79, 0xccd, 0xc31, 0xba3, 0xb21, 0xaab, 0xa3d, 0x9d9, 0x97b, 0x925, 0x8d4, 0x889, 0x842, 0x800,
            0x7c2, 0x788, 0x750, 0x71c, 0x6eb, 0x6bd, 0x690, 0x666, 0x63e, 0x618, 0x5f4, 0x5d1, 0x5b0, 0x591, 0x572, 0x555,
            0x539, 0x51f, 0x505, 0x4ec, 0x4d5, 0x4be, 0x4a8, 0x492, 0x47e, 0x46a, 0x457, 0x444, 0x432, 0x421, 0x410, 0x400,
        };

        private static readonly int[] finalPointPositionTable = { 0, 32, 16, 8, 17, 4, 18, 9, 19, 2, 20, 10, 21, 5, 22, 11, 23, 1, 24, 12, 25, 6, 26, 13, 27, 3, 28, 14, 29, 7, 30, 15, 31 };
        private static readonly int[] loopStart = { 1, 1, 17, 9, 9, 5, 5, 5, 5, 3, 3, 3, 3, 3, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 };
        private static readonly int[] loopEnd = { 0, 0, 17, 17, 25, 25, 25, 25, 29, 29, 29, 29, 29, 29, 29, 29, 31, 31, 31, 31, 31, 31, 31, 31, 31, 31, 31, 31, 31, 31, 31, 31, 32 };

        private struct Ctx { public uint invFloor, invCeil, halfFraction; public int numHalfPoints, splitPointOnFloor; }

        // Output: domain points (u, v; w = 1 - u - v) and clockwise triangles.
        internal readonly float[] U = new float[65 * 65], V = new float[65 * 65];
        internal readonly int[] Index = new int[64 * 64 * 2 * 3];
        internal int NumPoints, NumIndices;

        private bool patched;
        private int pInsideDelta, pInsideBad, pInsideRepl, pOutsideBase, pOutsideDelta, pOutsideBad, pOutsideRepl;

        // ---------------------------------------------------------------- D3D float/fixed helpers
        private static bool IsNaN(float a) => float.IsNaN(a);
        private static float Flush(float a)
        {
            int u = BitConverter.SingleToInt32Bits(a);
            int b = u & 0x7fffffff;
            if (b < 0x00800000) return BitConverter.Int32BitsToSingle(u & unchecked((int)0x80000000));
            return a;
        }
        private static float FMin(float a, float b)
        {
            float _a = Flush(a), _b = Flush(b);
            if (IsNaN(_b)) return a;
            if (_a == 0 && _b == 0) return (BitConverter.SingleToInt32Bits(_a) & unchecked((int)0x80000000)) != 0 ? a : b;
            return _a < _b ? a : b;
        }
        private static float FMax(float a, float b)
        {
            float _a = Flush(a), _b = Flush(b);
            if (IsNaN(_b)) return a;
            if (_a == 0 && _b == 0) return (BitConverter.SingleToInt32Bits(_b) & unchecked((int)0x80000000)) != 0 ? a : b;
            return _a >= _b ? a : b;
        }

        // floatToIDotF<15, 16, unsigned>
        private static uint FloatToFixed(float input)
        {
            const int cI = 15, cF = 16;
            const int mBits = 23, eBits = 8, bias = 127, hidden = 1 << mBits, mMask = hidden - 1;
            const int eMask = ((1 << eBits) - 1) << mBits, signBit = 1 << (eBits + mBits);
            const int minResult = 0, maxResult = ~minResult;
            int maxPos = (bias + cI) << mBits;
            int iShift = mBits + 1 - cI - cF;
            if (iShift >= 0) maxPos -= 1 << iShift;
            int maxNeg = 0;
            int output = BitConverter.SingleToInt32Bits(input);
            int unbiasedExponent = ((output & eMask) >> mBits) - bias;
            int isNegative = output & signBit;
            if (unbiasedExponent == bias + 1 && (output & mMask) != 0) output = 0;
            else if (isNegative == 0 && output >= maxPos) output = maxResult;
            else if (isNegative != 0 && (output & ~signBit) >= maxNeg) output = minResult;
            else if (unbiasedExponent < -cF - 1) output = 0;
            else
            {
                output = (output & mMask) | hidden;
                int extraBits = mBits - cF - unbiasedExponent;
                if (extraBits >= 0)
                {
                    if (isNegative != 0) output = ~output + 1;
                    int LSB = 1 << extraBits, extraMask = LSB - 1, half = LSB >> 1;
                    if ((output & LSB) != 0 || (output & extraMask) > half) output += half;
                    output >>= extraBits;
                }
                else
                {
                    output <<= -extraBits;
                    if (isNegative != 0) output = ~output + 1;
                }
            }
            return unchecked((uint)output);
        }

        private static float FixedToFloat(uint input) => (float)(input >> FXP_FRACTION_BITS) + (float)(input & FXP_FRACTION_MASK) / (1 << FXP_FRACTION_BITS);
        private static uint FxpCeil(uint input) => (input & FXP_FRACTION_MASK) != 0 ? (input & FXP_INTEGER_MASK) + FXP_ONE : input;
        private static uint FxpFloor(uint input) => input & FXP_INTEGER_MASK;

        private static int RemoveMSB(int val)
        {
            int check;
            if (val <= 0x0000ffff) check = val <= 0x000000ff ? 0x00000080 : 0x00008000;
            else check = val <= 0x00ffffff ? 0x00800000 : unchecked((int)0x80000000);
            for (int i = 0; i < 8; i++, check = (int)((uint)check >> 1)) if ((val & check) != 0) return val & ~check;
            return 0;
        }

        // ---------------------------------------------------------------- odd parity only (fractional_odd)
        private static int NumPointsForTessFactor(uint fxpTessFactor) => (int)((FxpCeil(FXP_ONE_HALF + (fxpTessFactor + 1) / 2) * 2) >> FXP_FRACTION_BITS);

        private static void ComputeTessFactorContext(uint fxpTessFactor, out Ctx c)
        {
            c = default;
            uint fxpHalf = (fxpTessFactor + 1) / 2;
            fxpHalf += FXP_ONE_HALF;   // Odd()
            uint floorHalf = FxpFloor(fxpHalf), ceilHalf = FxpCeil(fxpHalf);
            c.halfFraction = fxpHalf - floorHalf;
            c.numHalfPoints = (int)(ceilHalf >> FXP_FRACTION_BITS);
            if (ceilHalf == floorHalf) c.splitPointOnFloor = c.numHalfPoints + 1;
            else if (floorHalf == FXP_ONE) c.splitPointOnFloor = 0;
            else c.splitPointOnFloor = (RemoveMSB((int)(floorHalf >> FXP_FRACTION_BITS) - 1) << 1) + 1;
            int numFloorSegments = (int)((floorHalf * 2) >> FXP_FRACTION_BITS) - 1;
            int numCeilSegments = (int)((ceilHalf * 2) >> FXP_FRACTION_BITS) - 1;
            c.invFloor = s_fixedReciprocal[numFloorSegments];
            c.invCeil = s_fixedReciprocal[numCeilSegments];
        }

        private static uint PlacePointIn1D(in Ctx c, int point)
        {
            bool flip;
            if (point >= c.numHalfPoints) { point = (c.numHalfPoints << 1) - point - 1; flip = true; }   // Odd(): -1
            else flip = false;
            if (point == c.numHalfPoints) return FXP_ONE_HALF;
            uint onCeil = (uint)point, onFloor = onCeil;
            if (point > c.splitPointOnFloor) onFloor -= 1;
            uint locFloor = unchecked(onFloor * c.invFloor), locCeil = unchecked(onCeil * c.invCeil);
            uint loc = unchecked(locFloor * (FXP_ONE - c.halfFraction) + locCeil * c.halfFraction);
            loc = unchecked(loc + FXP_ONE_HALF) >> FXP_FRACTION_BITS;
            if (flip) loc = FXP_ONE - loc;
            return loc;
        }

        private void DefinePoint(uint u, uint v, int offset) { U[offset] = FixedToFloat(u); V[offset] = FixedToFloat(v); }

        private int PatchIndexValue(int index)
        {
            if (!patched) return index;
            if (index >= pOutsideBase) return index == pOutsideBad ? pOutsideRepl : index + pOutsideDelta;
            return index == pInsideBad ? pInsideRepl : index + pInsideDelta;
        }

        private void DefineIndex(int index, int offset) => Index[offset] = PatchIndexValue(index);
        private void DefineClockwiseTriangle(int i0, int i1, int i2, int baseOffset) { DefineIndex(i0, baseOffset); DefineIndex(i1, baseOffset + 1); DefineIndex(i2, baseOffset + 2); }

        // ---------------------------------------------------------------- the triangle domain
        internal void TessellateTri(float tessFactor_Ueq0, float tessFactor_Veq0, float tessFactor_Weq0, float insideTessFactor)
        {
            NumPoints = 0; NumIndices = 0; patched = false;
            if (!(tessFactor_Ueq0 > 0) || !(tessFactor_Veq0 > 0) || !(tessFactor_Weq0 > 0)) return;   // culled
            float lower = MIN_ODD, upper = MAX_ODD;
            tessFactor_Ueq0 = FMin(upper, FMax(lower, tessFactor_Ueq0));
            tessFactor_Veq0 = FMin(upper, FMax(lower, tessFactor_Veq0));
            tessFactor_Weq0 = FMin(upper, FMax(lower, tessFactor_Weq0));
            const float minOddPlusHalfEpsilon = MIN_ODD + EPSILON / 2;
            if (tessFactor_Ueq0 > minOddPlusHalfEpsilon || tessFactor_Veq0 > minOddPlusHalfEpsilon || tessFactor_Weq0 > minOddPlusHalfEpsilon)
                lower = MIN_ODD + EPSILON;   // force picture frame
            insideTessFactor = FMin(upper, FMax(lower, insideTessFactor));

            var outsideF = new[] { FloatToFixed(tessFactor_Ueq0), FloatToFixed(tessFactor_Veq0), FloatToFixed(tessFactor_Weq0) };
            uint insideF = FloatToFixed(insideTessFactor);
            if (insideF == FXP_ONE && outsideF[0] == FXP_ONE && outsideF[1] == FXP_ONE && outsideF[2] == FXP_ONE)
            {
                DefinePoint(0, FXP_ONE, 0); DefinePoint(0, 0, 1); DefinePoint(FXP_ONE, 0, 2);
                NumPoints = 3;
                DefineClockwiseTriangle(0, 1, 2, 0);
                NumIndices = 3;
                return;
            }
            var outsideCtx = new Ctx[TRI_EDGES];
            for (int e = 0; e < TRI_EDGES; e++) ComputeTessFactorContext(outsideF[e], out outsideCtx[e]);
            ComputeTessFactorContext(insideF, out var insideCtx);
            var numPointsForOutsideEdge = new int[TRI_EDGES];
            for (int e = 0; e < TRI_EDGES; e++) { numPointsForOutsideEdge[e] = NumPointsForTessFactor(outsideF[e]); NumPoints += numPointsForOutsideEdge[e]; }
            NumPoints -= 3;
            int numPointsForInside = Math.Max(4, NumPointsForTessFactor(insideF));   // Odd(): at least 4
            int insideEdgePointBaseOffset = NumPoints;
            {
                int numInteriorRings = (numPointsForInside >> 1) - 1;
                NumPoints += TRI_EDGES * (numInteriorRings * (numInteriorRings + 1) - numInteriorRings);
            }

            // Points (TriGeneratePoints)
            int pointOffset = 0;
            for (int edge = 0; edge < TRI_EDGES; edge++)
            {
                int parity = edge & 1, endPoint = numPointsForOutsideEdge[edge] - 1;
                for (int p = 0; p < endPoint; p++, pointOffset++)
                {
                    int q = parity != 0 ? p : endPoint - p;
                    uint param = PlacePointIn1D(outsideCtx[edge], q);
                    if (edge == 0) DefinePoint(0, param, pointOffset);
                    else DefinePoint(param, edge == 2 ? FXP_ONE - param : 0, pointOffset);
                }
            }
            int numRings = numPointsForInside >> 1;
            for (int ring = 1; ring < numRings; ring++)
            {
                int startPoint = ring, endPoint = numPointsForInside - 1 - startPoint;
                for (int edge = 0; edge < TRI_EDGES; edge++)
                {
                    int parity = edge & 1;
                    uint perp = PlacePointIn1D(insideCtx, startPoint);
                    perp = unchecked(perp * FXP_TWO_THIRDS);
                    perp = unchecked(perp + FXP_ONE_HALF) >> FXP_FRACTION_BITS;
                    for (int p = startPoint; p < endPoint; p++, pointOffset++)
                    {
                        int q = parity != 0 ? p : endPoint - (p - startPoint);
                        uint param = PlacePointIn1D(insideCtx, q);
                        const uint deriv = 2;
                        switch (edge)
                        {
                            case 0: DefinePoint(perp, unchecked(param - (perp + 1) / deriv), pointOffset); break;
                            case 1: DefinePoint(unchecked(param - (perp + 1) / deriv), perp, pointOffset); break;
                            default: DefinePoint(unchecked(param - (perp + 1) / deriv), unchecked(FXP_ONE - (param - (perp + 1) / deriv) - perp), pointOffset); break;
                        }
                    }
                }
            }

            // Connectivity (TriGenerateConnectivity)
            int numRingsC = (numPointsForInside + 1) >> 1;
            var ctxForEdge = new[] { outsideCtx[0], outsideCtx[1], outsideCtx[2] };
            var numPtsOutside = (int[])numPointsForOutsideEdge.Clone();
            int insideBase = insideEdgePointBaseOffset, outsideBase = 0;
            for (int ring = 1; ring < numRingsC; ring++)
            {
                int numPointsForInsideEdge = numPointsForInside - 2 * ring;
                int edge0InsideBase = insideBase, edge0OutsideBase = outsideBase;
                for (int edge = 0; edge < TRI_EDGES; edge++)
                {
                    int numTriangles = numPointsForInsideEdge + numPtsOutside[edge] - 2;
                    int insideBaseOffset, outsideBaseOffset;
                    if (edge == 2)
                    {
                        pInsideDelta = insideBase;
                        pInsideBad = numPointsForInsideEdge - 1;
                        pInsideRepl = edge0InsideBase;
                        pOutsideBase = pInsideBad + 1;
                        pOutsideDelta = outsideBase - pOutsideBase;
                        pOutsideBad = pOutsideBase + numPtsOutside[edge] - 1;
                        pOutsideRepl = edge0OutsideBase;
                        patched = true;
                        insideBaseOffset = 0; outsideBaseOffset = pOutsideBase;
                    }
                    else { insideBaseOffset = insideBase; outsideBaseOffset = outsideBase; }
                    if (ring == 1) StitchTransition(NumIndices, insideBaseOffset, insideCtx.numHalfPoints, outsideBaseOffset, ctxForEdge[edge].numHalfPoints);
                    else StitchRegularMirroredTrapezoid(NumIndices, numPointsForInsideEdge, insideBaseOffset, outsideBaseOffset);
                    if (edge == 2) patched = false;
                    NumIndices += numTriangles * 3;
                    outsideBase += numPtsOutside[edge] - 1;
                    insideBase += numPointsForInsideEdge - 1;
                    numPtsOutside[edge] = numPointsForInsideEdge;
                }
                if (ring == 1) for (int e = 0; e < TRI_EDGES; e++) ctxForEdge[e] = insideCtx;
            }
            DefineClockwiseTriangle(outsideBase, outsideBase + 1, outsideBase + 2, NumIndices);   // Odd(): centre triangle
            NumIndices += 3;
        }

        private void StitchRegularMirroredTrapezoid(int baseIndexOffset, int numInsideEdgePoints, int insidePoint, int outsidePoint)
        {
            DefineClockwiseTriangle(outsidePoint, outsidePoint + 1, insidePoint, baseIndexOffset); baseIndexOffset += 3; outsidePoint++;
            int p;
            for (p = 0; p < numInsideEdgePoints / 2; p++)
            {
                DefineClockwiseTriangle(outsidePoint, insidePoint + 1, insidePoint, baseIndexOffset); baseIndexOffset += 3;
                DefineClockwiseTriangle(outsidePoint, outsidePoint + 1, insidePoint + 1, baseIndexOffset); baseIndexOffset += 3;
                insidePoint++; outsidePoint++;
            }
            for (; p < numInsideEdgePoints - 1; p++)
            {
                DefineClockwiseTriangle(insidePoint, outsidePoint, outsidePoint + 1, baseIndexOffset); baseIndexOffset += 3;
                DefineClockwiseTriangle(insidePoint, outsidePoint + 1, insidePoint + 1, baseIndexOffset); baseIndexOffset += 3;
                insidePoint++; outsidePoint++;
            }
            DefineClockwiseTriangle(outsidePoint, outsidePoint + 1, insidePoint, baseIndexOffset);
        }

        // Both parities are odd here (fractional_odd everywhere).
        private void StitchTransition(int baseIndexOffset, int insidePoint, int insideNumHalf, int outsidePoint, int outsideNumHalf)
        {
            insideNumHalf -= 1; outsideNumHalf -= 1;
            int iStart = Math.Min(loopStart[insideNumHalf], loopStart[outsideNumHalf]);
            int iEnd = Math.Max(loopEnd[insideNumHalf], loopEnd[outsideNumHalf]);
            if (finalPointPositionTable[0] < outsideNumHalf)
            { DefineClockwiseTriangle(outsidePoint, outsidePoint + 1, insidePoint, baseIndexOffset); baseIndexOffset += 3; outsidePoint++; }
            for (int i = iStart; i <= iEnd; i++)
            {
                if (finalPointPositionTable[i] < insideNumHalf) { DefineClockwiseTriangle(insidePoint, outsidePoint, insidePoint + 1, baseIndexOffset); baseIndexOffset += 3; insidePoint++; }
                if (finalPointPositionTable[i] < outsideNumHalf) { DefineClockwiseTriangle(outsidePoint, outsidePoint + 1, insidePoint, baseIndexOffset); baseIndexOffset += 3; outsidePoint++; }
            }
            // Same (odd) parity: quad in the middle.
            DefineClockwiseTriangle(insidePoint, outsidePoint, insidePoint + 1, baseIndexOffset); baseIndexOffset += 3;
            DefineClockwiseTriangle(insidePoint + 1, outsidePoint, outsidePoint + 1, baseIndexOffset); baseIndexOffset += 3;
            insidePoint++; outsidePoint++;
            for (int i = iEnd; i >= iStart; i--)
            {
                if (finalPointPositionTable[i] < outsideNumHalf) { DefineClockwiseTriangle(outsidePoint, outsidePoint + 1, insidePoint, baseIndexOffset); baseIndexOffset += 3; outsidePoint++; }
                if (finalPointPositionTable[i] < insideNumHalf) { DefineClockwiseTriangle(insidePoint, outsidePoint, insidePoint + 1, baseIndexOffset); baseIndexOffset += 3; insidePoint++; }
            }
            if (finalPointPositionTable[0] < outsideNumHalf)
            { DefineClockwiseTriangle(outsidePoint, outsidePoint + 1, insidePoint, baseIndexOffset); }
        }
    }
}
