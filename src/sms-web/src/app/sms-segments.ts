// Display-only mirror of the server's MessageBodyRules. The server is the source of truth.

export const MAX_BODY_LENGTH = 670;

const GSM_BASIC =
  '@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞ\u001BÆæßÉ !"#¤%&\'()*+,-./0123456789:;<=>?' +
  '¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà';
const GSM_EXTENDED = '^{}\\[~]|€\f';

export function countSegments(body: string): number {
  if (body.length === 0) return 0;

  let septets = 0;
  for (const c of body) {
    if (GSM_BASIC.includes(c)) septets += 1;
    else if (GSM_EXTENDED.includes(c)) septets += 2;
    else {
      // Unicode (UCS-2), e.g. Hebrew. body.length counts UTF-16 code units, like the server.
      return body.length <= 70 ? 1 : Math.ceil(body.length / 67);
    }
  }
  return septets <= 160 ? 1 : Math.ceil(septets / 153);
}
