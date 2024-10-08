﻿// Copyright 2022 Carl Reinke
//
// This file is part of a program that is licensed under the terms of the GNU
// General Public License Version 3 as published by the Free Software
// Foundation.

// Contributors (in alphabetical order): [1]
//  * Daniel J. Bernstein
//  * Niels Duif
//  * Tanja Lange,
//  * Peter Schwabe
//  * Bo-Yin Yang
// The Ed25519 software is in the public domain. [2]
// [1] https://ed25519.cr.yp.to/
// [2] https://ed25519.cr.yp.to/software.html

using static supercop.crypto_sign.ed25519.ref10.fe;

#pragma warning disable CA1704 // Identifiers should be spelled correctly
#pragma warning disable CA1707 // Identifiers should not contain underscores

namespace supercop.crypto_sign.ed25519.ref10;

internal static partial class ge
{
    /*
    r = p
    */

    internal static void ge_p1p1_to_p2(out ge_p2 r, in ge_p1p1 p)
    {
        fe_mul(out r.X, in p.X, in p.T);
        fe_mul(out r.Y, in p.Y, in p.Z);
        fe_mul(out r.Z, in p.Z, in p.T);
    }
}
