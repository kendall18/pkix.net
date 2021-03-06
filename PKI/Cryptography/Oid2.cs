﻿using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using PKI.Structs;
using PKI.Utils;
using SysadminsLV.PKI.Win32;

namespace System.Security.Cryptography {
    /// <summary>
    /// An extended class for <see cref="Oid"/> class. Extended class provides rich functionality by returning additional OID registration information
    /// and OID registration/unregistration capabilites.
    /// </summary>
    public sealed class Oid2 {
        static readonly String _baseDsPath =
            $"CN=OID, CN=Public Key Services, CN=Services,{DsUtils.ConfigContext}";
        readonly Boolean _cng;
        readonly String _searchBy;
        String[] urls;
        Int32 flags;

        Oid2() { }
        /// <summary>
        /// Initializes a new instance of the Oid2 class using the specified Oid friendly name or value and search conditions.
        /// </summary>
        /// <param name="oid">Specifies the object identifier friendly name or value to search.</param>
        /// <param name="searchInDirectory">
        /// Specifies whether to search for an object identifier in Active Directory. If the machine is not
        /// domain-joined, an OID is searched by using local registration information.
        /// </param>
        /// <remarks>
        /// If registration information is found in Active Directory, <strong>DistinguishedName</strong> parameter contains
        /// directory path to a OID registration entry.
        /// </remarks>
        public Oid2(String oid, Boolean searchInDirectory) : this(oid, OidGroupEnum.AllGroups, searchInDirectory) { }
        /// <summary>
        /// Initializes a new instance of the Oid2 class using the specified Oid friendly name or value, OID registration group and search conditions.
        /// </summary>
        /// <param name="oid">Specifies the object identifier friendly name or value to search.</param>
        /// <param name="group">Specifies the OID registration group to search.</param>
        /// <param name="searchInDirectory">Specifies whether to search for an object identifier in Active Directory. If the machine is not
        /// domain-joined, an OID is searched by using local registration information.</param>
        public Oid2(String oid, OidGroupEnum group, Boolean searchInDirectory) {
            try {
                CryptoConfig.EncodeOID(oid);
                _searchBy = "ByValue";
            } catch { _searchBy = "ByName"; }
            if (Environment.OSVersion.Version.Major >= 6) { _cng = true; }
            if (searchInDirectory) {
                if (DsUtils.Ping()) { initializeDS(oid, group); } else { initializeLocal(oid, group); }
            } else {
                initializeLocal(oid, group);
            }
        }
        /// <summary>
        /// Initializes a new instance of the Oid2 class from an existing <see cref="Oid"/> object.
        /// </summary>
        /// <param name="oid">Existing object identifier.</param>
        /// <param name="searchInDirectory">
        /// Specifies whether to search for an object identifier in Active Directory. If the machine is not
        /// domain-joined, an OID is searched by using local registration information.
        /// </param>
        /// <remarks>
        /// If registration information is found in Active Directory, <strong>DistinguishedName</strong> parameter contains
        /// directory path to a OID registration entry.
        /// </remarks>
        public Oid2(Oid oid, Boolean searchInDirectory) : this(oid.Value, searchInDirectory) { }
        /// <summary>
        /// Initializes a new instance of the Oid2 class from an existing <see cref="Oid"/> object, OID registration group and search conditions.
        /// </summary>
        /// <param name="oid">Specifies the object identifier friendly name or value to search.</param>
        /// <param name="group">Specifies the OID registration group to search.</param>
        /// <param name="searchInDirectory">Specifies whether to search for an object identifier in Active Directory. If the machine is not
        /// domain-joined, an OID is searched by using local registration information.</param>
        public Oid2(Oid oid, OidGroupEnum group, Boolean searchInDirectory) : this(oid.Value, group, searchInDirectory) { }


        /// <summary>
        /// Gets the friendly name of the identifier.
        /// </summary>
        public String FriendlyName { get; set; }
        /// <summary>
        /// Gets the dotted number of the identifier.
        /// </summary>
        public String Value { get; set; }
        /// <summary>
        /// Gets the registration path in Active Directory.
        /// </summary>
        public String DistinguishedName { get; set; }
        /// <summary>
        /// Gets the group at which the identifier is registered
        /// </summary>
        public OidGroupEnum OidGroup { get; set; }

        void initializeLocal(String oid, OidGroupEnum group) {
            IntPtr ptr, oidptr;
            if (_searchBy.ToLower() == "byvalue") {
                oidptr = Marshal.StringToHGlobalAnsi(oid);
                ptr = Crypt32.CryptFindOIDInfo(Wincrypt.CRYPT_OID_INFO_OID_KEY, oidptr, (UInt32)group);
            } else {
                oidptr = Marshal.StringToHGlobalUni(oid);
                ptr = Crypt32.CryptFindOIDInfo(Wincrypt.CRYPT_OID_INFO_NAME_KEY, oidptr, (UInt32)group);
            }
            if (ptr.Equals(IntPtr.Zero)) { return; }
            if (_cng) {
                Wincrypt.CRYPT_OID_INFO OidInfo = (Wincrypt.CRYPT_OID_INFO)Marshal.PtrToStructure(ptr, typeof(Wincrypt.CRYPT_OID_INFO));
                FriendlyName = OidInfo.pwszName;
                Value = OidInfo.pszOID;
                OidGroup = (OidGroupEnum)OidInfo.dwGroupId;
            } else {
                Wincrypt.CRYPT_OID_INFO_Win2k3 OidInfo = (Wincrypt.CRYPT_OID_INFO_Win2k3)Marshal.PtrToStructure(ptr, typeof(Wincrypt.CRYPT_OID_INFO_Win2k3));
                FriendlyName = OidInfo.pwszName;
                Value = OidInfo.pszOID;
                OidGroup = (OidGroupEnum)OidInfo.dwGroupId;
            }
            Marshal.FreeHGlobal(oidptr);
        }
        void initializeDS(String oid, OidGroupEnum group) {
            List<Int32> exclude = new List<Int32>(new[] { 1, 2, 3, 4, 5, 6, 10 });
            if (exclude.Contains((Int32)group)) {
                initializeLocal(oid, group);
                return;
            }
            Boolean found = false;
            String oidvalue = oid;
            if (_searchBy.ToLower() == "byname") {
                Oid oidobj = new Oid(oid);
                if (String.IsNullOrEmpty(oidobj.Value)) { return; }
                oidvalue = oidobj.Value;
            }
            String cn = computeOidHash(oidvalue);
            String ldapPath = $"CN={cn},{_baseDsPath}";
            try {
                IDictionary<String, Object> oidInDs = DsUtils.GetEntryProperties(
                    ldapPath,
                    DsUtils.PropFlags,
                    DsUtils.PropDN,
                    DsUtils.PropDisplayName,
                    DsUtils.PropCpsOid);
                found = true;
                DistinguishedName = (String)oidInDs[DsUtils.PropDN];
                flags = (Int32)oidInDs[DsUtils.PropFlags];
                FriendlyName = (String)oidInDs[DsUtils.PropDisplayName];
                switch (flags) {
                    case 1:
                        if (group != OidGroupEnum.AllGroups && group != OidGroupEnum.CertificateTemplate) {
                            throw new Exception("Oid type mismatch.");
                        }
                        OidGroup = OidGroupEnum.CertificateTemplate;
                        break;
                    case 2:
                        if (group != OidGroupEnum.AllGroups && group != OidGroupEnum.IssuancePolicy) {
                            throw new Exception("Oid type mismatch.");
                        }
                        OidGroup = OidGroupEnum.IssuancePolicy;
                        if (oidInDs[DsUtils.PropCpsOid] == null) {
                            break;
                        }
                        try {
                            Object[] cps = (Object[])oidInDs[DsUtils.PropCpsOid];
                            urls = cps.Cast<String>().ToArray();
                        } catch {
                            urls = new[] { (String)oidInDs[DsUtils.PropCpsOid] };
                        }
                        break;
                    case 3:
                        if (group != OidGroupEnum.AllGroups && group != OidGroupEnum.ApplicationPolicy) {
                            throw new Exception("Oid type mismatch.");
                        }
                        OidGroup = OidGroupEnum.ApplicationPolicy;
                        break;
                }
            } catch {
                FriendlyName = String.Empty;
                Value = String.Empty;
                OidGroup = OidGroupEnum.AllGroups;
                DistinguishedName = String.Empty;
            }
            if (!found) { initializeLocal(oid, group); }
        }
        Boolean Equals(Oid2 other) {
            return String.Equals(Value, other.Value)
                && OidGroup == other.OidGroup
                && String.Equals(FriendlyName, other.FriendlyName);
        }

        static void registerLocal(Oid oid, OidGroupEnum group) {
            Boolean CNG = Environment.OSVersion.Version.Major >= 6;
            if (CNG) {
                registerCNG(oid, group);
            } else {
                registerLegacy(oid, group);
            }
        }
        static void registerDS(Oid oid, OidGroupEnum group, CultureInfo localeId, String cpsUrl) {
            String cn = computeOidHash(oid.Value);
            String entryDN =
                DsUtils.AddEntry(
                    _baseDsPath,
                    $"CN={cn}",
                    DsUtils.SchemaObjectIdentifier);
            switch (group) {
                case OidGroupEnum.ApplicationPolicy:
                    DsUtils.SetEntryProperty(entryDN, DsUtils.PropFlags, 3);
                    break;
                case OidGroupEnum.IssuancePolicy:
                    DsUtils.SetEntryProperty(entryDN, DsUtils.PropFlags, 2);
                    if (!String.IsNullOrEmpty(cpsUrl)) {
                        DsUtils.SetEntryProperty(entryDN, DsUtils.PropCpsOid, cpsUrl);
                    }
                    break;
            }
            DsUtils.SetEntryProperty(entryDN, DsUtils.PropCertTemplateOid, oid.Value);
            if (localeId == null) {
                DsUtils.SetEntryProperty(entryDN, DsUtils.PropDisplayName, oid.FriendlyName);
            } else {
                DsUtils.SetEntryProperty(entryDN, DsUtils.PropLocalizedOid, $"{localeId.LCID},{oid.FriendlyName}");
            }
        }
        static void registerLegacy(Oid oid, OidGroupEnum group) {
            Wincrypt.CRYPT_OID_INFO_Win2k3 oidinfo = new Wincrypt.CRYPT_OID_INFO_Win2k3 {
                cbSize = Marshal.SizeOf(typeof(Wincrypt.CRYPT_OID_INFO_Win2k3)),
                pszOID = oid.Value,
                pwszName = oid.FriendlyName,
                dwGroupId = (Int32)group
            };
            if (!Crypt32.CryptRegisterOIDInfo(oidinfo, 0)) {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        static void registerCNG(Oid oid, OidGroupEnum group) {
            Wincrypt.CRYPT_OID_INFO oidinfo = new Wincrypt.CRYPT_OID_INFO {
                cbSize = Marshal.SizeOf(typeof(Wincrypt.CRYPT_OID_INFO)),
                pszOID = oid.Value,
                pwszName = oid.FriendlyName,
                dwGroupId = (Int32)group
            };
            if (!Crypt32.CryptRegisterOIDInfo(oidinfo, 0)) {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        static Boolean unregisterLocal(IEnumerable<Oid2> oids) {
            Boolean CNG = Environment.OSVersion.Version.Major >= 6;
            if (CNG) {
                unregisterCNG(oids);
            } else {
                unregisterLegacy(oids);
            }
            return true;
        }
        static Boolean unregisterDS(String oid, OidGroupEnum group) {
            String cn = computeOidHash(oid);
            String ldapPath = $"CN={cn},{_baseDsPath}";
            Int32 flags = (Int32)DsUtils.GetEntryProperty(ldapPath, DsUtils.PropFlags);
            switch (group) {
                case OidGroupEnum.ApplicationPolicy:
                    if (flags != 3) { return false; }
                    break;
                case OidGroupEnum.IssuancePolicy:
                    if (flags != 2) { return false; }
                    break;
                case OidGroupEnum.CertificateTemplate:
                    if (flags != 1) { return false; }
                    break;
            }
            DsUtils.RemoveEntry(ldapPath);
            return true;
        }
        static void unregisterLegacy(IEnumerable<Oid2> oid) {
            if (oid.Select(oid2 => new Wincrypt.CRYPT_OID_INFO_Win2k3 {
                cbSize = Marshal.SizeOf(typeof(Wincrypt.CRYPT_OID_INFO_Win2k3)),
                pszOID = oid2.Value,
                pwszName = oid2.FriendlyName,
                dwGroupId = (Int32)oid2.OidGroup
            }).Any(oidinfo => !Crypt32.CryptUnregisterOIDInfo(oidinfo))) {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        static void unregisterCNG(IEnumerable<Oid2> oid) {
            if (oid.Select(oid2 => new Wincrypt.CRYPT_OID_INFO {
                cbSize = Marshal.SizeOf(typeof(Wincrypt.CRYPT_OID_INFO)),
                pszOID = oid2.Value,
                pwszName = oid2.FriendlyName,
                dwGroupId = (Int32)oid2.OidGroup
            }).Any(oidinfo => !Crypt32.CryptUnregisterOIDInfo(oidinfo))) {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        static String computeOidHash(String oid) {
            String[] tokens = oid.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            String LastArc = tokens[tokens.Length - 1];
            if (LastArc.Length >= 16) { LastArc = LastArc.Substring(0, 16); }
            MD5 hasher = MD5.Create();
            Byte[] bytes = hasher.ComputeHash(Encoding.Unicode.GetBytes(oid));
            StringBuilder hexstring = new StringBuilder();
            foreach (Byte hashbyte in bytes) {
                hexstring.Append($"{hashbyte:X2}");
            }
            hasher.Clear();
            return LastArc + "." + hexstring;
        }

        /// <summary>
        /// Serves as a hash function for a particular type.
        /// </summary>
        /// <returns>The hash code for the <strong>Oid2</strong> as an integer.</returns>
        public override Int32 GetHashCode() {
            unchecked {
                Int32 hashCode = Value?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ (Int32)OidGroup;
                hashCode = (hashCode * 397) ^ (FriendlyName?.GetHashCode() ?? 0);
                return hashCode;
            }
        }
        /// <summary>
        /// Compares two <strong>Oid2</strong> objects for equality.
        /// </summary>
        /// <param name="obj">An <strong>Oid2</strong> object to compare to the current object.</param>
        /// <returns>
        /// <strong>True</strong> if the current <strong>Oid2</strong> object is equal to the object specified by the other parameter;
        /// otherwise, <strong>False</strong>.
        /// </returns>
        /// <remarks>
        /// Two objects are considered equal if they are <strong>Oid2</strong> objects and they have the same
        /// friendly name, Oid value and they belongs to the same Oid group.
        /// </remarks>
        public override Boolean Equals(Object obj) {
            if (ReferenceEquals(null, obj)) { return false; }
            if (ReferenceEquals(this, obj)) { return true; }
            return obj.GetType() == GetType() && Equals((Oid2)obj);
        }
        /// <summary>
        /// Gets an array of URL associated with certificate practice statement (<strong>CPS</strong>). This method fails on any OID groups except <strong>IssuancePolicy</strong>.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The current OID object is not registered in the <strong>IssuancePolicy</strong> OID group.
        /// </exception>
        /// <returns>An array of URL strings.</returns>
        public String[] GetCPSLinks() {
            if (OidGroup == OidGroupEnum.IssuancePolicy && !String.IsNullOrEmpty(Value)) {
                return urls;
            }
            throw new InvalidOperationException("The object is not in the valid state.");
        }
        /// <summary>
        /// Gets a generic <see cref="Oid"/> object from the current object.
        /// </summary>
        /// <returns>An <see cref="Oid"/> object from the current object.</returns>
        public Oid ToOid() {
            return new Oid(Value, FriendlyName);
        }
        /// <summary>
        /// Converts hashing algorithm OID to appropriate OID from signature group. For example, translates
        /// <strong>sha1</strong> hashing algorithm to <strong>sha1NoSign</strong> with the same OID value.
        /// </summary>
        /// <param name="hashAlgorithm">Hashing algorithm</param>
        /// <exception cref="ArgumentNullException">
        /// <strong>hashAlgorithm</strong> parameter is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Input OID doesn't belong to hash algorithm group or it cannot be translated to a respective
        /// </exception>
        /// <returns>OID in signature group.</returns>
        public static Oid2 MapHashToSignatureOid(Oid2 hashAlgorithm) {
            if (hashAlgorithm == null) { throw new ArgumentNullException(nameof(hashAlgorithm)); }
            if (hashAlgorithm.OidGroup != OidGroupEnum.HashAlgorithm) {
                throw new ArgumentException("Input OID must belong to hashing group.");
            }
            Oid2 newOid = new Oid2(hashAlgorithm.Value, OidGroupEnum.SignatureAlgorithm, false);
            if (String.IsNullOrEmpty(newOid.Value)) {
                throw new ArgumentException("Cannot translate hashing algorithm to signature algorithm.");
            }
            return newOid;
        }

        /// <summary>
        /// Gets all registrations for the specified OID value.
        /// </summary>
        /// <param name="value">OID value to search. If the OID name is passed, it is converted to a best OID value
        /// match and performs OID search by it's value.</param>
        /// <param name="searchInDirectory">
        /// Specifies whether to search for an object identifier in Active Directory. If the machine is not
        /// domain-joined, an OID is searched by using local registration information.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The <strong>value</strong> parameter contains unresolvable object identifier friendly name.
        /// </exception>
        /// <returns>An array of OID registrations.</returns>
        /// <remarks>
        /// If registration information is found in Active Directory, <strong>DistinguishedName</strong> parameter contains
        /// directory path to a OID registration entry.
        /// </remarks>
        public static Oid2[] GetAllOids(String value, Boolean searchInDirectory) {
            String oidvalue;
            try {
                CryptoConfig.EncodeOID(value);
                oidvalue = value;
            } catch {
                Oid oid = new Oid(value);
                if (String.IsNullOrEmpty(oid.Value)) {
                    throw new ArgumentException("Specified OID value is not recognized.", nameof(value));
                }
                oidvalue = oid.Value;
            }
            return new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }.Select(group => new Oid2(oidvalue, (OidGroupEnum)group, searchInDirectory)).Where(obj => !String.IsNullOrEmpty(obj.Value)).ToArray();
        }
        /// <summary>
        /// Registers object identifier in the OID database, either, local or in Active Directory.
        /// </summary>
        /// <param name="value">An object identifier value to register.</param>
        /// <param name="friendlyName">A friendly name associated with the object identifier.</param>
        /// <param name="group">Specifies the OID group where specified object identifier should be registered.</param>
        /// <param name="writeInDirectory">Specifies, whether object is registered locally or in Active Directory.</param>
        /// <param name="localeId">
        ///		Specifies the locale ID. This parameter can be used to provide localized friendly name. This parameter can
        ///		be used only when <strong>writeInDirectory</strong> is set to <strong>True</strong> in other cases it is
        ///		silently ignored.
        /// </param>
        /// <param name="cpsUrl">
        ///		Specifies the URL to a <i>certificate practice statement</i> (<strong>CPS</strong>) location.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///		<strong>value</strong> and/or <strong>friendlyName</strong> is null or empty.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///		Specified OID group is not supported. See <strong>Remarks</strong> section for more details.
        /// </exception>
        /// <exception cref="InvalidDataException"><strong>value</strong> parameter is not object idnetifier value.</exception>
        /// <exception cref="NotSupportedException">
        ///		A caller chose OID registration in Active Directory, however, the current computer is not a member of any
        ///		Active Directory domain.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        ///		An object identifier is already registered.
        /// </exception>
        /// <remarks>
        /// <para>
        /// <strong>Permissions:</strong> for this method to succeed, the caller must be a member of the local
        /// administrators group (if <strong>writeInDirectory</strong> is set to <strong>False</strong>) or
        /// be a member of <strong>Enterprise Admins</strong> group or has delegated write permissions on the
        /// <strong>OID</strong> container in Active Directory. OID container location is
        /// <i>CN=OID, CN=Public Key Services, CN=Services,CN=Configuration, {Configuration naming context}</i>.
        /// </para>
        /// <para>
        ///		A newly registered OID is not resolvable by an application immediately. You may need to restart an application
        ///		to allow new OID lookup.
        /// </para>
        /// <para>
        ///		When <strong>writeInDirectory</strong> is set to <strong>True</strong>, <strong>group</strong> parameter
        ///		is limited only to one of the following value: <strong>ApplicationPolicy</strong>,<strong>IssuancePolicy</strong>
        ///		and <strong>CertificateTemplate</strong>. Other OID groups are not allowed to be stored in Active Directory.
        /// </para>
        /// </remarks>
        /// <returns>Registered object identifier.</returns>
        public static Oid2 Register(String value, String friendlyName, OidGroupEnum group, Boolean writeInDirectory, CultureInfo localeId, String cpsUrl = null) {
            if (String.IsNullOrEmpty(value)) { throw new ArgumentNullException(nameof(value)); }
            if (String.IsNullOrEmpty(friendlyName)) { throw new ArgumentNullException(nameof(friendlyName)); }
            try { CryptoConfig.EncodeOID(value); } catch { throw new InvalidDataException("The value is not valid OID string."); }
            String cn = null;
            if (writeInDirectory) {
                if (!DsUtils.Ping()) { throw new NotSupportedException("Workgroup environment is not supported."); }
                if (!String.IsNullOrEmpty(new Oid2(value, group, true).DistinguishedName)) {
                    throw new InvalidOperationException("The object already exist.");
                }
                List<Int32> exclude = new List<Int32>(new[] { 0, 1, 2, 3, 4, 5, 6, 9, 10 });
                if (exclude.Contains((Int32)group)) {
                    throw new ArgumentException("The OID group is not valid.");
                }
                registerDS(new Oid(value, friendlyName), group, localeId, cpsUrl);
                cn = "CN=" + computeOidHash(value) + ",CN=OID," + DsUtils.ConfigContext;
            } else {
                registerLocal(new Oid(value, friendlyName), group);
            }
            return new Oid2 {
                FriendlyName = friendlyName,
                Value = value,
                OidGroup = group,
                DistinguishedName = cn
            };
        }
        /// <summary>
        /// Unregisters object identifier from OID registration database.
        /// </summary>
        /// <param name="value">Specifies the object identifier value.</param>
        /// <param name="group">Specifies the OID group from which the OID is removed. </param>
        /// <param name="deleteFromDirectory">
        ///		Specifies whether to perform registration removal from Active Directory. If Active Directory is unavailable,
        ///		the method will attempt to unregister OID from a local OID registration database.
        /// </param>
        /// <exception cref="ArgumentNullException"><strong>value</strong> parameter is null or empty.</exception>
        /// <returns>
        ///		<strong>True</strong> if OID or OIDs were unregistered successfully. If specified OID information is not
        ///		registered, the method returns <strong>False</strong>. An exception is thrown when caller do not have
        ///		appropriate permissions. See <strong>Remarks</strong> section for additional details.
        /// </returns>
        /// <remarks>
        /// <strong>Permissions:</strong> a caller must have local administrator permissions in order to remove OID
        /// registration from local OID database. When <strong>deleteFromDirectory</strong> is set to <strong>True</strong>,
        /// a caller must be a member of <strong>Enterprise Admins</strong> group or have delegated permissions on a OID
        /// container in Active Directory. OID container location is
        /// <i>CN=OID, CN=Public Key Services, CN=Services,CN=Configuration, {Configuration naming context}</i>.
        /// </remarks>
        public static Boolean Unregister(String value, OidGroupEnum group, Boolean deleteFromDirectory) {
            if (String.IsNullOrEmpty(value)) { throw new ArgumentNullException(nameof(value)); }
            List<Oid2> oids = new List<Oid2>();
            if (group == OidGroupEnum.AllGroups) {
                try { oids.AddRange(GetAllOids(value, deleteFromDirectory)); } catch { return false; }
            } else {
                oids.Add(new Oid2(value, group, deleteFromDirectory));
                if (String.IsNullOrEmpty(oids[0].Value)) { return false; }
            }
            if (!deleteFromDirectory || !DsUtils.Ping()) {
                return unregisterLocal(oids);
            }
            List<Int32> valid = new List<Int32>(new[] { 0, 7, 8, 9 });
            if (oids.Where(oid => !String.IsNullOrEmpty(oid.DistinguishedName)).Any(oid => oid.OidGroup != group && group != OidGroupEnum.AllGroups)) {
                return false;
            }
            return valid.Contains((Int32)group) && unregisterDS(oids[0].Value, group);
        }
    }
}
