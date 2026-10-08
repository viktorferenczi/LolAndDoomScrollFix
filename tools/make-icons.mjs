// Egyszerű bővítményikonok (zöld kör óramutatókkal) előállítása külső függőség nélkül.
// Használat: node tools/make-icons.mjs extension/icons
import { writeFileSync, mkdirSync } from 'node:fs';
import { deflateSync } from 'node:zlib';
import { join } from 'node:path';

const outDir = process.argv[2] ?? 'extension/icons';
mkdirSync(outDir, { recursive: true });

const crcTable = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});
const crc32 = (buf) => {
  let c = 0xffffffff;
  for (const b of buf) c = crcTable[(c ^ b) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
};
const chunk = (type, data) => {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
};

function distToSegment(px, py, ax, ay, bx, by) {
  const dx = bx - ax, dy = by - ay;
  const t = Math.max(0, Math.min(1, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)));
  return Math.hypot(px - (ax + t * dx), py - (ay + t * dy));
}

function icon(size) {
  const rows = [];
  const c = size / 2, r = size * 0.46, w = Math.max(1.2, size * 0.09);
  for (let y = 0; y < size; y++) {
    const row = [0];
    for (let x = 0; x < size; x++) {
      const px = x + 0.5, py = y + 0.5;
      const d = Math.hypot(px - c, py - c);
      let a = Math.max(0, Math.min(1, r - d + 0.5));
      let [R, G, B] = [46, 160, 67];
      const hand = Math.min(
        distToSegment(px, py, c, c, c, c - r * 0.6),
        distToSegment(px, py, c, c, c + r * 0.4, c + r * 0.25));
      if (hand < w / 2 + 0.5) {
        const k = Math.max(0, Math.min(1, w / 2 + 0.5 - hand));
        R = R + (255 - R) * k; G = G + (255 - G) * k; B = B + (255 - B) * k;
      }
      row.push(R | 0, G | 0, B | 0, Math.round(a * 255));
    }
    rows.push(Buffer.from(row));
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(size, 0); ihdr.writeUInt32BE(size, 4);
  ihdr[8] = 8; ihdr[9] = 6; // 8 bit RGBA
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', deflateSync(Buffer.concat(rows))),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

for (const size of [16, 48, 128]) writeFileSync(join(outDir, `icon${size}.png`), icon(size));
console.log('Ikonok elkészültek:', outDir);
