﻿// Copyright 2022 Carl Reinke
//
// This file is part of a program that is licensed under the terms of the GNU
// General Public License version 3 as published by the Free Software
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

#pragma warning disable CA1704 // Identifiers should be spelled correctly
#pragma warning disable CA1707 // Identifiers should not contain underscores

namespace supercop.crypto_sign.ed25519.ref10
{
    internal struct ge_cached
    {
        internal fe YplusX;
        internal fe YminusX;
        internal fe Z;
        internal fe T2d;
    }
}
