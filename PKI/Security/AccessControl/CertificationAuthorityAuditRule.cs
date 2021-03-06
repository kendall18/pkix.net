﻿using System;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PKI.Security.AccessControl {
	/// <summary>
	/// Reserved.
	/// </summary>
	sealed class CertificationAuthorityAuditRule : AuditRule {

		public CertificationAuthorityAuditRule(
			IdentityReference identity,
			CertificationAuthorityRights accessMask,
			AuditFlags flags)
			: base(identity, AccessMaskFromRights(accessMask), false, InheritanceFlags.None, PropagationFlags.None, flags) { }

		public CertificationAuthorityRights CertificationAuthorityRights => RightsFromAccessMask(AccessMask);

		static CertificationAuthorityRights RightsFromAccessMask(Int32 accessMask) {
			return (CertificationAuthorityRights)accessMask;
		}
		static Int32 AccessMaskFromRights(CertificationAuthorityRights rights) {
			return (Int32)rights;
		}
	}
}
