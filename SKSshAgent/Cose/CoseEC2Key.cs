﻿// Copyright 2022 Carl Reinke
//
// This file is part of a program that is licensed under the terms of the GNU
// General Public License Version 3 as published by the Free Software
// Foundation.

using SKSshAgent;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;

namespace SKSshAgent.Cose;

internal sealed class CoseEC2Key : CoseKey
{
    private readonly ECParameters _ecParameters;

    /// <exception cref="ArgumentException"/>
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="CryptographicException"/>
    public CoseEC2Key(CoseAlgorithm algorithm, CoseEllipticCurve curve, ImmutableArray<byte> x, ImmutableArray<byte> y)
        : base(CoseKeyType.EC2, algorithm)
    {
        switch (algorithm)
        {
            case CoseAlgorithm.ES256:
            case CoseAlgorithm.ES384:
            case CoseAlgorithm.ES512:
                break;
            default:
                throw new ArgumentException("Invalid algorithm.", nameof(algorithm));
        }
        switch (curve)
        {
            case CoseEllipticCurve.P256:
            case CoseEllipticCurve.P384:
            case CoseEllipticCurve.P521:
                break;
            default:
                throw new ArgumentException("Invalid curve.", nameof(curve));
        }
        int fieldSizeBits = curve.GetFieldSizeBits();
        int fieldElementLength = MPInt.SizeBitsToLength(fieldSizeBits);
        if (x == null)
            throw new ArgumentNullException(nameof(x));
        if (x.Length != fieldElementLength || MPInt.GetBitLength(x.AsSpan()) > fieldSizeBits)
            throw new ArgumentException("Invalid EC field element.", nameof(x));
        if (y == null)
            throw new ArgumentNullException(nameof(y));
        if (y.Length != fieldElementLength || MPInt.GetBitLength(y.AsSpan()) > fieldSizeBits)
            throw new ArgumentException("Invalid EC field element.", nameof(y));

        _ecParameters = CreateParameters(curve, x.ToArray(), y.ToArray());
        _ecParameters.Validate();

        Curve = curve;
        X = x;
        Y = y;
    }

    /// <exception cref="CryptographicException"/>
    internal CoseEC2Key(CoseAlgorithm algorithm, CoseEllipticCurve curve, byte[] x, byte[] y)
        : base(CoseKeyType.EC2, algorithm)
    {
        _ecParameters = CreateParameters(curve, x, y);
        _ecParameters.Validate();

        Curve = curve;
        X = x.ToImmutableArray();
        Y = y.ToImmutableArray();
    }

    public CoseEllipticCurve Curve { get; }

    public ImmutableArray<byte> X { get; }

    public ImmutableArray<byte> Y { get; }

    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="ArgumentException"/>
    public bool VerifyData(ReadOnlySpan<byte> data, CoseEcdsaSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        if (signature.Algorithm != Algorithm || signature.Curve != Curve)
            throw new ArgumentException("Incompatible signature.", nameof(signature));

        byte[] signatureBytes = new byte[signature.R.Length + signature.S.Length];
        signature.R.CopyTo(signatureBytes);
        signature.S.CopyTo(signatureBytes, signature.R.Length);

        var hashAlgorithm = signature.Algorithm.GetHashAlgorithmName();

        using (var ecdsa = ECDsa.Create(_ecParameters))
            return ecdsa.VerifyData(data, signatureBytes, hashAlgorithm);
    }

    private static ECCurve GetECCurve(CoseEllipticCurve curve)
    {
        return curve switch
        {
            CoseEllipticCurve.P256 => ECCurve.NamedCurves.nistP256,
            CoseEllipticCurve.P384 => ECCurve.NamedCurves.nistP384,
            CoseEllipticCurve.P521 => ECCurve.NamedCurves.nistP521,
            _ => throw new UnreachableException(),
        };
    }

    private static ECParameters CreateParameters(CoseEllipticCurve curve, byte[] x, byte[] y)
    {
        ECCurve ecCurve = GetECCurve(curve);

        return new ECParameters
        {
            Curve = ecCurve,
            Q = new ECPoint
            {
                X = x,
                Y = y,
            }
        };
    }
}
