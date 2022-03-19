﻿// Copyright 2022 Carl Reinke
//
// This file is part of a program that is licensed under the terms of the GNU
// General Public License version 3 as published by the Free Software
// Foundation.

using Windows.Win32.Foundation;

namespace SKSshAgent
{
    internal sealed class OpenSshPipe : SshAgentPipe
    {
        public OpenSshPipe(HWND hWnd)
            : base(hWnd)
        {
        }

        public override string PipeName => @"openssh-ssh-agent";
    }
}