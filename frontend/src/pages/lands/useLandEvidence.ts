import { useQuery } from '@tanstack/react-query';

import { fetchDocuments } from '@/api/lands';
import { fetchDocumentTypes } from '@/api/reference';
import type { LandDocumentDto } from '@/types/lands';

/**
 * The document-evidence state of one land, computed exactly as the server derives it.
 *
 * Investor visibility is derived, never stored: a Verified land is shown only while the latest
 * version of every document type that is *currently* required for verification is Approved. A
 * required type with no uploaded version at all is outstanding (missing evidence is never a
 * pass), and an older version superseded by a newer one is history, not evidence. This mirrors
 * `LandEvidence` on the server; it decides nothing on its own and the server re-checks
 * everything on every guarded operation.
 *
 * The queries reuse the keys of the documents panel, so both consumers share one request and
 * one cache entry instead of fetching the same lists twice.
 */
export function useLandEvidence(landId: number, enabled = true) {
  const documentsQuery = useQuery({
    queryKey: ['land', landId, 'documents'],
    queryFn: () => fetchDocuments(landId),
    enabled,
  });

  const typesQuery = useQuery({
    queryKey: ['reference', 'documentTypes'],
    queryFn: fetchDocumentTypes,
    staleTime: 5 * 60_000,
  });

  const documents = documentsQuery.data ?? [];

  const requiredTypeIds = new Set(
    (typesQuery.data ?? [])
      .filter((type) => type.isRequiredForVerification)
      .map((type) => type.id),
  );

  // Only the latest version of a document type is the land's evidence, exactly as the server
  // treats it (an older version superseded by a newer one is history, not evidence).
  const latestByType = new Map<number, LandDocumentDto>();
  for (const document of documents) {
    const current = latestByType.get(document.documentTypeId);
    if (!current || document.version > current.version) {
      latestByType.set(document.documentTypeId, document);
    }
  }

  const approvedTypeIds = new Set(
    [...latestByType.values()]
      .filter((document) => document.status === 'Approved')
      .map((document) => document.documentTypeId),
  );

  // A required type is outstanding when it has no approved latest version: its latest version is
  // not approved, or no version has been uploaded at all. The server requires an approved latest
  // version for *every* required type, so a type with no uploaded version is missing evidence
  // (never a pass); the server hides the land from investors until it is approved again, and only
  // those types may be corrected by the owner.
  const outstandingTypeIds = new Set(
    [...requiredTypeIds].filter((typeId) => !approvedTypeIds.has(typeId)),
  );

  // The names are the server's own document-type names; nothing is derived or invented here.
  const outstandingTypeNames = (typesQuery.data ?? [])
    .filter((type) => outstandingTypeIds.has(type.id))
    .map((type) => type.name);

  return {
    documents,
    outstandingTypeIds,
    outstandingTypeNames,
    /** True when at least one required document type has no approved latest version. */
    outstandingEvidence: outstandingTypeIds.size > 0,
    /** True once both lists have actually loaded, so "outstanding" is not read from loading data. */
    evidenceReady: documentsQuery.isSuccess && typesQuery.isSuccess,
    documentsQuery,
    typesQuery,
  };
}
