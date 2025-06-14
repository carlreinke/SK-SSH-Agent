﻿// Copyright 2022 Carl Reinke
//
// This file is part of a program that is licensed under the terms of the GNU
// General Public License Version 3 as published by the Free Software
// Foundation.

using SKSshAgent.Cose;
using SKSshAgent.Ssh;
using SKSshAgent.WebAuthn;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Windows.Win32.Foundation;
using Windows.Win32.Networking.WindowsWebServices;
using static Windows.Win32.PInvoke;

namespace SKSshAgent;

internal static class WebAuthnApi
{
    public static readonly uint? Version;

    private static readonly HRESULT HRESULT_ERROR_CANCELLED = (HRESULT)0x800704C7;

    static WebAuthnApi()
    {
        try
        {
            Version = WebAuthNGetApiVersionNumber();
        }
        catch (DllNotFoundException)
        {
            Version = null;
        }
        catch (EntryPointNotFoundException)
        {
            Version = 0;
        }
    }

    /// <exception cref="NotSupportedException"/>
    /// <exception cref="ArgumentException"/>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="TimeoutException"/>
    /// <exception cref="InvalidDataException"/>
    /// <exception cref="Exception"/>
    public static MakeCredentialResult MakeCredential(
        HWND hWnd,
        string rpId,
        ReadOnlySpan<byte> userId,
        string userName,
        SshKeyTypeInfo keyTypeInfo,
        OpenSshSKFlags flags,
        ReadOnlySpan<byte> challenge,
        CancellationToken cancellationToken)
    {
        if (!(Version >= WEBAUTHN_API_VERSION_1))
            throw new NotSupportedException("Insufficient WebAuthn version.");

        int algorithm;
        if (keyTypeInfo == SshKeyTypeInfo.OpenSshSKEcdsaSha2NistP256)
            algorithm = WEBAUTHN_COSE_ALGORITHM_ECDSA_P256_WITH_SHA256;
        else if (keyTypeInfo == SshKeyTypeInfo.OpenSshSKEd25519)
            algorithm = (int)CoseAlgorithm.EdDsa;  // Missing API definition.
        else
            throw new ArgumentException("Invalid key type info.", nameof(keyTypeInfo));

        unsafe
        {
            WEBAUTHN_CREDENTIAL_ATTESTATION* credentialAttestation = null;

            try
            {
                fixed (char* credentialTypePubKeyPtr = WEBAUTHN_CREDENTIAL_TYPE_PUBLIC_KEY)
                {
                    WEBAUTHN_COSE_CREDENTIAL_PARAMETER[] credentialParams = new[]
                    {
                        new WEBAUTHN_COSE_CREDENTIAL_PARAMETER()
                        {
                            dwVersion = WEBAUTHN_COSE_CREDENTIAL_PARAMETER_CURRENT_VERSION,
                            pwszCredentialType = credentialTypePubKeyPtr,
                            lAlg = algorithm,
                        }
                    };

                    fixed (char* rpIdPtr = rpId)
                    fixed (char* rpNamePtr = "OpenSSH")
                    fixed (byte* userIdPtr = userId)
                    fixed (char* userNamePtr = userName)
                    fixed (char* userDisplayNamePtr = "")
                    fixed (WEBAUTHN_COSE_CREDENTIAL_PARAMETER* credentialParamsPtr = credentialParams)
                    fixed (char* hashAlgorithmSha256Ptr = WEBAUTHN_HASH_ALGORITHM_SHA_256)
                    fixed (byte* challengePtr = challenge)
                    {
                        var cancellationId = Guid.NewGuid();
                        var cancellationId2 = cancellationId;
                        using (cancellationToken.Register(() => _ = WebAuthNCancelCurrentOperation(cancellationId2)))
                        {
                            HRESULT hr = WebAuthNAuthenticatorMakeCredential(
                                hWnd: hWnd,
                                pRpInformation: new WEBAUTHN_RP_ENTITY_INFORMATION()
                                {
                                    dwVersion = WEBAUTHN_RP_ENTITY_INFORMATION_CURRENT_VERSION,
                                    pwszId = rpIdPtr,
                                    pwszName = rpNamePtr,
                                    pwszIcon = null,
                                },
                                pUserInformation: new WEBAUTHN_USER_ENTITY_INFORMATION()
                                {
                                    dwVersion = WEBAUTHN_USER_ENTITY_INFORMATION_CURRENT_VERSION,
                                    cbId = (uint)userId.Length,
                                    pbId = userIdPtr,
                                    pwszName = userNamePtr,
                                    pwszIcon = null,
                                    pwszDisplayName = userDisplayNamePtr,
                                },
                                pPubKeyCredParams: new WEBAUTHN_COSE_CREDENTIAL_PARAMETERS()
                                {
                                    cCredentialParameters = (uint)credentialParams.Length,
                                    pCredentialParameters = credentialParamsPtr,
                                },
                                pWebAuthNClientData: new WEBAUTHN_CLIENT_DATA()
                                {
                                    dwVersion = WEBAUTHN_CLIENT_DATA_CURRENT_VERSION,
                                    cbClientDataJSON = (uint)challenge.Length,
                                    pbClientDataJSON = challengePtr,
                                    pwszHashAlgId = hashAlgorithmSha256Ptr,
                                },
                                pWebAuthNMakeCredentialOptions: new WEBAUTHN_AUTHENTICATOR_MAKE_CREDENTIAL_OPTIONS()
                                {
                                    dwVersion = WEBAUTHN_AUTHENTICATOR_MAKE_CREDENTIAL_OPTIONS_CURRENT_VERSION,
                                    dwTimeoutMilliseconds = 60 * 1000,
                                    CredentialList = new WEBAUTHN_CREDENTIALS()
                                    {
                                        cCredentials = 0,
                                        pCredentials = null,
                                    },
                                    // OpenSSH always requires the "credProtect" extension for the "verify-required"
                                    // and "resident" options but this is not strictly necessary, as the authenticator
                                    // attests to whether user verification was performed such that user verification
                                    // can be enforced at the server via the "verify-required" option.
                                    Extensions = new WEBAUTHN_EXTENSIONS()
                                    {
                                        cExtensions = 0,
                                        pExtensions = null,
                                    },
                                    dwAuthenticatorAttachment = WEBAUTHN_AUTHENTICATOR_ATTACHMENT_ANY,
                                    bRequireResidentKey = false,  // TODO: "resident" option
                                    dwUserVerificationRequirement = flags.HasFlag(OpenSshSKFlags.UserVerificationRequired)
                                        ? WEBAUTHN_USER_VERIFICATION_REQUIREMENT_REQUIRED
                                        : WEBAUTHN_USER_VERIFICATION_REQUIREMENT_DISCOURAGED,
                                    dwAttestationConveyancePreference = WEBAUTHN_ATTESTATION_CONVEYANCE_PREFERENCE_NONE,
                                    dwFlags = 0,
                                    pCancellationId = &cancellationId,
                                    pExcludeCredentialList = null,
                                    dwEnterpriseAttestation = WEBAUTHN_ENTERPRISE_ATTESTATION_NONE,
                                    dwLargeBlobSupport = WEBAUTHN_LARGE_BLOB_SUPPORT_NONE,
                                    bPreferResidentKey = false,
                                },
                                ppWebAuthNCredentialAttestation: out credentialAttestation);

                            ThrowIfError(hr);
                        }

#if DEBUG
                        Debug.WriteLine("MakeCredential Challenge: " + Convert.ToHexString(challenge));
                        Debug.WriteLine("MakeCredential Authenticator data: " + Convert.ToHexString(new ReadOnlySpan<byte>(credentialAttestation->pbAuthenticatorData, (int)credentialAttestation->cbAuthenticatorData)));
                        Debug.WriteLine("MakeCredential Attestation format: " + credentialAttestation->pwszFormatType.ToString());
                        Debug.WriteLine("MakeCredential Attestation statement: " + Convert.ToHexString(new ReadOnlySpan<byte>(credentialAttestation->pbAttestation, (int)credentialAttestation->cbAttestation)));
#endif

                        var authenticatorDataSpan = new ReadOnlySpan<byte>(credentialAttestation->pbAuthenticatorData, (int)credentialAttestation->cbAuthenticatorData);
                        var authenticatorData = WebAuthnAuthenticatorData.Parse(authenticatorDataSpan, out int authenticatorDataBytesUsed);
                        if (authenticatorDataBytesUsed < credentialAttestation->cbAuthenticatorData)
                            throw new InvalidDataException("Excess data.");

                        return new MakeCredentialResult(authenticatorData);
                    }
                }
            }
            finally
            {
                if (credentialAttestation != null)
                    WebAuthNFreeCredentialAttestation(credentialAttestation);
            }
        }
    }

    /// <exception cref="NotSupportedException"/>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="TimeoutException"/>
    /// <exception cref="InvalidDataException"/>
    /// <exception cref="Exception"/>
    public static GetAssertionResult GetAssertion(
        HWND hWnd,
        CoseKey key,
        string rpId,
        ReadOnlySpan<byte> keyHandle,
        OpenSshSKFlags flags,
        ReadOnlySpan<byte> challenge,
        CancellationToken cancellationToken)
    {
        if (!(Version >= WEBAUTHN_API_VERSION_1))
            throw new NotSupportedException("Insufficient WebAuthn version.");

        unsafe
        {
            WEBAUTHN_ASSERTION* assertion = null;

            try
            {
                fixed (byte* keyHandlePtr = keyHandle)
                fixed (char* credentialTypePubKeyPtr = WEBAUTHN_CREDENTIAL_TYPE_PUBLIC_KEY)
                {
                    WEBAUTHN_CREDENTIAL[] credentials = new[]
                    {
                        new WEBAUTHN_CREDENTIAL()
                        {
                            dwVersion = WEBAUTHN_CREDENTIAL_CURRENT_VERSION,
                            cbId = (uint)keyHandle.Length,
                            pbId = keyHandlePtr,
                            pwszCredentialType = credentialTypePubKeyPtr,
                        }
                    };

                    fixed (byte* challengePtr = challenge)
                    fixed (char* hashAlgorithmSha256Ptr = WEBAUTHN_HASH_ALGORITHM_SHA_256)
                    fixed (WEBAUTHN_CREDENTIAL* credentialPtr = credentials)
                    {
                        var cancellationId = Guid.NewGuid();
                        var cancellationId2 = cancellationId;
                        using (cancellationToken.Register(() => _ = WebAuthNCancelCurrentOperation(cancellationId2)))
                        {
                            HRESULT hr = WebAuthNAuthenticatorGetAssertion(
                                hWnd: hWnd,
                                pwszRpId: rpId,
                                pWebAuthNClientData: new WEBAUTHN_CLIENT_DATA
                                {
                                    dwVersion = WEBAUTHN_CLIENT_DATA_CURRENT_VERSION,
                                    cbClientDataJSON = (uint)challenge.Length,
                                    pbClientDataJSON = challengePtr,
                                    pwszHashAlgId = hashAlgorithmSha256Ptr,
                                },
                                pWebAuthNGetAssertionOptions: new WEBAUTHN_AUTHENTICATOR_GET_ASSERTION_OPTIONS
                                {
                                    dwVersion = WEBAUTHN_AUTHENTICATOR_GET_ASSERTION_OPTIONS_CURRENT_VERSION,
                                    dwTimeoutMilliseconds = 60 * 1000,
                                    CredentialList = new WEBAUTHN_CREDENTIALS()
                                    {
                                        cCredentials = (uint)credentials.Length,
                                        pCredentials = credentialPtr,
                                    },
                                    Extensions = new WEBAUTHN_EXTENSIONS()
                                    {
                                        cExtensions = 0,
                                        pExtensions = null,
                                    },
                                    dwAuthenticatorAttachment = WEBAUTHN_AUTHENTICATOR_ATTACHMENT_ANY,
                                    dwUserVerificationRequirement = flags.HasFlag(OpenSshSKFlags.UserVerificationRequired)
                                        ? WEBAUTHN_USER_VERIFICATION_REQUIREMENT_REQUIRED
                                        : WEBAUTHN_USER_VERIFICATION_REQUIREMENT_DISCOURAGED,
                                    dwFlags = 0,
                                    pwszU2fAppId = null,
                                    pbU2fAppId = null,
                                    pCancellationId = &cancellationId,
                                    pAllowCredentialList = null,
                                    dwCredLargeBlobOperation = WEBAUTHN_CRED_LARGE_BLOB_OPERATION_NONE,
                                    cbCredLargeBlob = 0,
                                    pbCredLargeBlob = null,
                                },
                                ppWebAuthNAssertion: out assertion);

                            ThrowIfError(hr);
                        }

#if DEBUG
                        Debug.WriteLine("GetAssertion Challenge: " + Convert.ToHexString(challenge));
                        Debug.WriteLine("GetAssertion Authenticator data: " + Convert.ToHexString(new ReadOnlySpan<byte>(assertion->pbAuthenticatorData, (int)assertion->cbAuthenticatorData)));
                        Debug.WriteLine("GetAssertion Signature: " + Convert.ToHexString(new ReadOnlySpan<byte>(assertion->pbSignature, (int)assertion->cbSignature)));
#endif

                        var authenticatorDataSpan = new ReadOnlySpan<byte>(assertion->pbAuthenticatorData, (int)assertion->cbAuthenticatorData);
                        var authenticatorData = WebAuthnAuthenticatorData.Parse(authenticatorDataSpan, out int authenticatorDataBytesUsed);
                        if (authenticatorDataBytesUsed < assertion->cbAuthenticatorData)
                            throw new InvalidDataException("Excess data.");

                        var signatureSpan = new ReadOnlySpan<byte>(assertion->pbSignature, (int)assertion->cbSignature);
                        var signature = WebAuthnSignature.Parse(key, signatureSpan, out int signatureBytesUsed);
                        if (signatureBytesUsed < assertion->cbSignature)
                            throw new InvalidDataException("Excess data.");

#if DEBUG
                        byte[] signedData = WebAuthnSignature.GetSignedData(challenge, authenticatorDataSpan);

                        bool verified;
                        switch (key.KeyType)
                        {
                            case CoseKeyType.Okp:
                            {
                                var okpKey = (CoseOkpKey)key;

                                switch (signature.Algorithm)
                                {
                                    case CoseAlgorithm.EdDsa:
                                    {
                                        var edDsaSignature = (CoseEdDsaSignature)signature;

                                        verified = okpKey.VerifyData(signedData, edDsaSignature);
                                        break;
                                    }
                                    default:
                                        throw new UnreachableException();
                                }
                                break;
                            }
                            case CoseKeyType.EC2:
                            {
                                var ec2Key = (CoseEC2Key)key;

                                switch (signature.Algorithm)
                                {
                                    case CoseAlgorithm.ES256:
                                    case CoseAlgorithm.ES384:
                                    case CoseAlgorithm.ES512:
                                    {
                                        var ecdsaSignature = (CoseEcdsaSignature)signature;

                                        verified = ec2Key.VerifyData(signedData, ecdsaSignature);
                                        break;
                                    }
                                    default:
                                        throw new UnreachableException();
                                }
                                break;
                            }
                            default:
                                throw new UnreachableException();
                        }
                        Debug.Assert(verified);
#endif

                        return new GetAssertionResult(authenticatorData, signature);
                    }
                }
            }
            finally
            {
                if (assertion != null)
                    WebAuthNFreeAssertion(assertion);
            }
        }
    }

    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="NotSupportedException"/>
    /// <exception cref="TimeoutException"/>
    /// <exception cref="Exception"/>
    private static void ThrowIfError(HRESULT hr)
    {
        // https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-erref/32cce05d-3a39-4c7e-8f66-5e788e1107cf

        if (hr == HRESULT_ERROR_CANCELLED)
            throw new OperationCanceledException();
        else if (hr == HRESULT.NTE_NOT_SUPPORTED)
            throw new NotSupportedException("The requested operation is not supported.");
        else if (hr == HRESULT.RPC_E_TIMEOUT)
            throw new TimeoutException("The requested operation timed out.");
        else if (hr != 0)
            throw new Exception($"0x{hr.Value:X8}: {WebAuthNGetErrorName(hr)}") { HResult = hr };
    }

    public sealed class MakeCredentialResult
    {
        internal MakeCredentialResult(WebAuthnAuthenticatorData authenticatorData)
        {
            AuthenticatorData = authenticatorData;
        }

        public WebAuthnAuthenticatorData AuthenticatorData { get; }
    }

    public sealed class GetAssertionResult
    {
        internal GetAssertionResult(WebAuthnAuthenticatorData authenticatorData, CoseSignature signature)
        {
            AuthenticatorData = authenticatorData;
            Signature = signature;
        }

        public WebAuthnAuthenticatorData AuthenticatorData { get; }

        public CoseSignature Signature { get; }
    }
}
