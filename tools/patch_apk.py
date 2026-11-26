import io
import json
import struct
import sys
import zlib

sys.stdout.reconfigure(encoding='utf-8')

APK_PATH = r'F:\冒挖h5(1).apk.1'
OUT_DIR = r'C:\Temp\aandm116\dist'

# ----------------------------------------------------------------
# 1) dex 补丁：把 MainActivity.onPause 里对 WebView.onPause() 的调用
#    替换为 nop（息屏后 WebView 定时器不再被暂停，配合无声保活即可挂机）。
# ----------------------------------------------------------------

def dex_patch_nop_web_onpause(dex_bytes):
    def u16(b, o):
        return struct.unpack_from('<H', b, o)[0]

    # method id of WebView;->onPause()V —— 用 androguard 直接查表
    from androguard.core.dex import DEX
    d0 = DEX(dex_bytes)
    items = d0.get_methods_id_item()
    target_idx = None
    i = 0
    while True:
        try:
            it = items.get(i)
        except Exception:
            break
        try:
            if it.get_class_name() == 'Landroid/webkit/WebView;' and it.get_name() == 'onPause':
                target_idx = i
                break
        except Exception:
            break
        i += 1
    if target_idx is None:
        raise RuntimeError('WebView.onPause method id not found')
    print('WebView.onPause method idx =', target_idx)

    # scan for invoke-virtual (0x6E) referencing this method idx
    hits = []
    for off in range(0, len(dex_bytes) - 6):
        if dex_bytes[off] == 0x6E and u16(dex_bytes, off + 2) == target_idx:
            hits.append(off)
    print('invoke-virtual WebView.onPause occurrences:', [hex(h) for h in hits])

    # locate MainActivity.onPause code range via androguard
    pause_range = None
    for cls in d0.get_classes():
        if cls.get_name() != 'Lcom/shenyu/maowa/h5/MainActivity;':
            continue
        for m in cls.get_methods():
            code = m.get_code()
            if not code or m.get_name() != 'onPause':
                continue
            base_off = code.get_off()
            insns_off = base_off + 16
            size = code.get_insns_size() * 2
            pause_range = (insns_off, insns_off + size)
    if pause_range is None:
        raise RuntimeError('MainActivity.onPause code range not found')
    print('onPause code range:', hex(pause_range[0]), '-', hex(pause_range[1]))

    to_patch = [h for h in hits if pause_range[0] <= h < pause_range[1]]
    print('patching inside onPause:', [hex(h) for h in to_patch])
    if len(to_patch) != 1:
        raise RuntimeError('expected exactly 1 call in onPause, found %d' % len(to_patch))
    off = to_patch[0]
    patched = bytearray(dex_bytes)
    patched[off:off + 6] = b'\x00' * 6  # invoke-virtual 是 3 个 code unit（6 字节），全部 nop
    # DEX 头部自校验：重算 SHA-1 签名（偏移 12 起）与 Adler32 校验和
    import hashlib as _hl
    import zlib as _zl
    sig = _hl.sha1(patched[12:]).digest()
    patched[12:32] = sig
    checksum = _zl.adler32(bytes(patched[12:])) & 0xffffffff
    struct.pack_into('<I', patched, 8, checksum)

    # verify: re-parse, ensure onPause no longer calls WebView.onPause (super call kept)
    d2 = DEX(bytes(patched))
    ok = True
    for cls in d2.get_classes():
        if cls.get_name() == 'Lcom/shenyu/maowa/h5/MainActivity;':
            for m in cls.get_methods():
                if m.get_name() == 'onPause':
                    for ins in m.get_code().get_bc().get_instructions():
                        out = ins.get_output()
                        if 'Landroid/webkit/WebView;->onPause()V' in out:
                            ok = False
                if m.get_name() == 'launch':
                    found = False
                    for ins in m.get_code().get_bc().get_instructions():
                        if ins.get_name() == 'invoke-virtual' and 'Landroid/webkit/WebView;->onPause()V' in ins.get_output():
                            found = True
                    assert found, 'launch() must still contain its onPause call'
    if not ok:
        raise RuntimeError('post-patch verification failed')
    print('dex patch verified (onPause no longer pauses WebView; launch untouched)')
    return bytes(patched)


# ----------------------------------------------------------------
# 2) index.html：等长改写（1215 字节），注入 /aandm/assistant.js 加载器
# ----------------------------------------------------------------

def build_index_html(original, total_len, loader):
    text = original.decode('utf-8')
    text = text.replace(loader, '')  # idempotent
    # 先按原结构压缩空白
    text = text.replace('\r\n', '\n')
    text = text.replace('>\\s*<', '><')
    lines = [l.strip() for l in text.split('\n')]
    text = ''.join(l for l in lines if l)
    # 拼接：在 </body> 前放加载器
    body_end = text.find('</body>')
    assert body_end > 0
    compact = text[:body_end] + loader + text[body_end:]
    # 保留必要结构后，逐段压缩到 total_len 以内
    def squeeze(t):
        t = t.replace('<!doctype html>', '<!doctype html>')
        t = t.replace('<html lang="zh-CN">', '<html lang=zh-CN>')
        t = t.replace('<meta charset="UTF-8" />', '<meta charset=UTF-8>')
        for a, b in [
            (' <link', '<link'), (' <title', '<title'), (' <script', '<script'),
            (' <main', '<main'), (' <canvas', '<canvas'), (' <div', '<div'),
            (' <aside', '<aside'), (' </main>', '</main>'), (' </body>', '</body>'),
            (' </html>', '</html>'), (' </head>', '</head>'),
        ]:
            t = t.replace(a, b)
        t = t.replace(' type="module"', ' type=module')
        t = t.replace(' rel="icon"', ' rel=icon').replace(' rel="apple-touch-icon"', ' rel=apple-touch-icon')
        t = t.replace(' rel="stylesheet"', ' rel=stylesheet')
        t = t.replace(' type="image/png"', ' type=image/png')
        t = t.replace(' aria-label="冒险与挖矿"', '')
        t = t.replace(' aria-label="游戏画面"', '')
        return t
    compact = squeeze(compact)
    if len(compact.encode('utf-8')) > total_len:
        raise RuntimeError('minified index.html (%d bytes) still exceeds %d' % (
            len(compact.encode('utf-8')), total_len))
    # 用空格补齐到精确长度（放在 </html> 之前无效，放在末尾 OK：HTML 尾部空白无害）
    cur = len(compact.encode('utf-8'))
    pad = total_len - cur
    compact = compact + ' ' * pad
    raw = compact.encode('utf-8')
    assert len(raw) == total_len, (len(raw), total_len)
    assert loader.encode('utf-8') in raw
    return raw


# ----------------------------------------------------------------
# 3) ZIP 重建（保持每条目压缩方式；存储条目 4 字节对齐；写入正确 CRC）
# ----------------------------------------------------------------

class ZipWriter:
    def __init__(self):
        self.blobs = []   # (name, method, data, align)
        self.entries = []  # for central directory

    def add(self, name, data, method, align=1):
        self.blobs.append((name, method, data, align))

    def build(self):
        out = bytearray()
        central = bytearray()
        for name, method, data, align in self.blobs:
            crc = zlib.crc32(data) & 0xffffffff
            if method == 0:
                payload = data
            else:
                co = zlib.compressobj(9, zlib.DEFLATED, -15)
                payload = co.compress(data) + co.flush()
            name_bytes = name.encode('utf-8')
            offset = len(out)
            if align > 1:
                # data 起始位置 = offset + 30 + len(name) + len(extra)
                need = (offset + 30 + len(name_bytes)) % align
                if need:
                    extra_len = align - need
                else:
                    extra_len = 0
            else:
                extra_len = 0
            extra = b'\x00' * extra_len
            out += struct.pack('<IHHHHHIIIHH', 0x04034b50, 20, 0, method, 0, 0,
                               crc, len(payload), len(data), len(name_bytes), extra_len)
            out += name_bytes + extra + payload
            central += struct.pack('<IHHHHHHIIIHHHHHII', 0x02014b50, 20, 20, 0, method, 0, 0,
                                   crc, len(payload), len(data), len(name_bytes), extra_len,
                                   0, 0, 0, 0, offset)
            central += name_bytes + extra
        cd_offset = len(out)
        out += central
        cd_size = len(out) - cd_offset
        out += struct.pack('<IHHHHIIH', 0x06054b50, 0, 0, len(self.blobs), len(self.blobs),
                           cd_size, cd_offset, 0)
        return bytes(out)


def rebuild_apk(src_zip, replacements, index_html_raw):
    import zipfile as zf
    src = zf.ZipFile(src_zip)
    w = ZipWriter()
    for info in src.infolist():
        name = info.filename
        data = src.read(name)
        method = 0 if info.compress_type == zf.ZIP_STORED else 8
        if name in replacements:
            data = replacements[name]
        align = 4 if (method == 0 and (name.endswith('.arsc') or name.endswith('.png'))) else 1
        w.add(name, data, method, align)
    src.close()
    return w.build()


# ----------------------------------------------------------------
# 4) APK Signature Scheme v2（纯 Python：RSA-PKCS1-SHA256，块级 SHA-256 摘要）
# ----------------------------------------------------------------

def len_prefixed(data):
    return struct.pack('<I', len(data)) + data


def seq_of(items):
    body = b''.join(items)
    return len_prefixed(body)


def compute_v2_digests(entries_region, cd_region, eocd_bytes):
    """apksig 规范：所有分区的 1MB 分块摘要串到一个 0x5a + 总块数 的头下。"""
    CHUNK = 1048576
    all_digests = []
    for region in (entries_region, cd_region, eocd_bytes):
        for i in range(0, len(region), CHUNK):
            chunk = region[i:i + CHUNK]
            all_digests.append(hashlib_sha256(struct.pack('<BI', 0xa5, len(chunk)) + chunk))
    return b'\x5a' + struct.pack('<I', len(all_digests)) + b''.join(all_digests)


import hashlib
def hashlib_sha256(data):
    return hashlib.sha256(data).digest()


def build_v2_block(entries_region, cd_region, eocd_bytes, key_pem, cert_der):
    from cryptography.hazmat.primitives import serialization, hashes
    from cryptography.hazmat.primitives.asymmetric import padding
    key = serialization.load_pem_private_key(key_pem, password=None)

    digest = compute_v2_digests(entries_region, cd_region, eocd_bytes)

    SIG_ALGO = 0x0103  # RSASSA-PKCS1-v1_5 with SHA2-256
    digests = seq_of([len_prefixed(struct.pack('<I', SIG_ALGO) + len_prefixed(digest))])
    certs = seq_of([len_prefixed(cert_der)])
    # 剥离保护属性：0xbeeff00d + uint32(2) —— 声明本包依赖 v2，防 META-INF 剥离重签
    attrs_value = len_prefixed(struct.pack('<I', 0xbeeff00d) + len_prefixed(struct.pack('<I', 2)))
    attributes = seq_of([attrs_value])
    signed_data = digests + certs + attributes

    signature = key.sign(signed_data, padding.PKCS1v15(), hashes.SHA256())
    signatures = seq_of([len_prefixed(struct.pack('<I', SIG_ALGO) + len_prefixed(signature))])
    public_key = len_prefixed(key.public_key().public_bytes(
        serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo))

    signer = seq_of([len_prefixed(signed_data), signatures, public_key])
    signers = seq_of([signer])

    def make_pair(pid, value):
        body = struct.pack('<I', pid) + value
        return struct.pack('<Q', len(body)) + body

    pairs = make_pair(0x7109871a, signers)
    total = len(pairs) + 8 + 16
    block = struct.pack('<Q', total) + pairs + struct.pack('<Q', total) + b'APK Sig Block 42'
    return block


def locate_eocd(file_bytes):
    """从尾部按注释长度一致性定位 EOCD（PNG 数据里可能出现伪 PK\\x05\\x06）。"""
    pos = len(file_bytes) - 22
    while pos >= 0:
        if file_bytes[pos:pos + 4] == b'PK\x05\x06':
            comment = struct.unpack_from('<H', file_bytes, pos + 20)[0]
            if pos + 22 + comment == len(file_bytes):
                return pos
        pos -= 1
    return -1


def verify_v2(file_bytes):
    """自校验：找到签名块并验证签名与摘要。"""
    from cryptography.hazmat.primitives import serialization, hashes
    from cryptography.hazmat.primitives.asymmetric import padding
    import struct as st
    # EOCD：从尾部按注释长度一致性定位（PNG 数据里可能出现伪 PK\x05\x06）
    eocd = locate_eocd(file_bytes)
    assert eocd != -1, 'EOCD not found'
    cd_size = st.unpack_from('<I', file_bytes, eocd + 12)[0]
    cd_off = st.unpack_from('<I', file_bytes, eocd + 16)[0]
    block_end = cd_off
    magic = file_bytes[block_end - 16:block_end]
    assert magic == b'APK Sig Block 42', magic
    size2 = st.unpack_from('<Q', file_bytes, block_end - 24)[0]
    block_start = block_end - size2 - 8  # 第一 size 字段本身占 8 字节，不计入 size 值
    size1 = st.unpack_from('<Q', file_bytes, block_start)[0]
    assert size1 == size2
    p = block_start + 8
    pairs_end = block_end - 24
    v2_value = None
    while p < pairs_end:
        plen = st.unpack_from('<Q', file_bytes, p)[0]
        pid = st.unpack_from('<I', file_bytes, p + 8)[0]
        if pid == 0x7109871a:
            v2_value = file_bytes[p + 12:p + 8 + plen]
        p += 8 + plen
    assert v2_value is not None, 'v2 block not found'

    def read_lp(buf, off):
        n = st.unpack_from('<I', buf, off)[0]
        return buf[off + 4:off + 4 + n], off + 4 + n

    signers, _ = read_lp(v2_value, 0)
    signer, _ = read_lp(signers, 0)
    signed_data, o = read_lp(signer, 0)
    signatures, o = read_lp(signer, o)
    public_key, o = read_lp(signer, o)
    digests, o2 = read_lp(signed_data, 0)
    certs, o2 = read_lp(signed_data, o2)
    attrs, o2 = read_lp(signed_data, o2)

    sig_item, _ = read_lp(signatures, 0)
    sig_algo = st.unpack_from('<I', sig_item, 0)[0]
    assert sig_algo == 0x0103
    sig, _ = read_lp(sig_item, 4)
    digest_item, _ = read_lp(digests, 0)
    assert st.unpack_from('<I', digest_item, 0)[0] == 0x0103
    expected_digest, _ = read_lp(digest_item, 4)

    key = serialization.load_der_public_key(public_key)
    key.verify(sig, signed_data, padding.PKCS1v15(), hashes.SHA256())

    # 重算摘要：entries=[0..签名块起点)，CD=[CD实际起点..EOCD)，EOCD(CD偏移=签名块起点)
    eocd_fixed = bytearray(file_bytes[eocd:])
    st.pack_into('<I', eocd_fixed, 16, block_start)
    actual = compute_v2_digests(file_bytes[:block_start], file_bytes[cd_off:eocd],
                                bytes(eocd_fixed))
    print('v2 verify: block_start=%#x cd_off=%#x eocd=%#x' % (block_start, cd_off, eocd))
    print('v2 verify: expected head=%s actual head=%s' % (
        expected_digest[:16].hex(), actual[:16].hex()))
    assert actual == expected_digest, 'v2 digest mismatch'
    return True


# ----------------------------------------------------------------
# 主流程
# ----------------------------------------------------------------

def main():
    import zipfile as zf
    import os

    # A. dex 补丁
    src = zf.ZipFile(APK_PATH)
    dex_bytes = src.read('classes.dex')
    dex_patched = dex_patch_nop_web_onpause(dex_bytes)

    # B. index.html 等长改写
    mp = json.loads(src.read('assets/bundled.manifest.json').decode('utf-8'))
    entry = mp['files']['/index.html']
    blob_name = 'assets/web/' + entry['sha256']
    original_html = src.read(blob_name)
    LOADER = '<script src="/aandm/assistant.js"></script>'
    html_patched = build_index_html(original_html, entry['bytes'], LOADER)
    print('index.html rewritten: %d bytes (was %d), loader=%d bytes' % (
        len(html_patched), len(original_html), len(LOADER)))

    # C. 重建 ZIP
    out_apk = os.path.join(OUT_DIR, 'maowa-h5-assistant-unsigned.apk')
    data = rebuild_apk(APK_PATH, {'classes.dex': dex_patched, blob_name: html_patched}, None)
    with open(out_apk, 'wb') as f:
        f.write(data)
    print('rebuilt:', out_apk, len(data), 'bytes')

    # D. v2 签名
    from cryptography.hazmat.primitives.asymmetric import rsa
    from cryptography import x509
    from cryptography.x509.oid import NameOID
    from cryptography.hazmat.primitives import hashes, serialization
    import datetime

    key_path = os.path.join(OUT_DIR, 'assistant-release-key.pem')
    cert_path = os.path.join(OUT_DIR, 'assistant-release-cert.pem')
    if not os.path.exists(key_path):
        key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
        subject = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, u'AandM Assistant')])
        cert = (x509.CertificateBuilder()
                .subject_name(subject).issuer_name(subject)
                .public_key(key.public_key())
                .serial_number(x509.random_serial_number())
                .not_valid_before(datetime.datetime(2026, 1, 1))
                .not_valid_after(datetime.datetime(2046, 1, 1))
                .sign(key, hashes.SHA256()))
        with open(key_path, 'wb') as f:
            f.write(key.private_bytes(serialization.Encoding.PEM,
                                      serialization.PrivateFormat.PKCS8,
                                      serialization.NoEncryption()))
        with open(cert_path, 'wb') as f:
            f.write(cert.public_bytes(serialization.Encoding.DER))
        print('generated new release key/cert')
    key_pem = open(key_path, 'rb').read()
    cert_der = open(cert_path, 'rb').read()

    # 在 CD 前插入签名块（摘要区域：entries=补丁 raw 的 CD 之前；CD 段原样；EOCD 修正 CD 偏移）
    raw = open(out_apk, 'rb').read()
    eocd = locate_eocd(raw)
    assert eocd != -1
    cd_off = struct.unpack_from('<I', raw, eocd + 16)[0]
    entries_region = raw[:cd_off]
    cd_region = raw[cd_off:eocd]
    eocd_fixed = bytearray(raw[eocd:])
    struct.pack_into('<I', eocd_fixed, 16, cd_off)  # 最终文件中 CD 起点 = 签名块起点 = 原 cd_off
    block = build_v2_block(entries_region, cd_region, bytes(eocd_fixed), key_pem, cert_der)
    signed = bytearray(raw[:cd_off] + block + raw[cd_off:])
    # 修正 EOCD 的 CD 偏移 → 指向插入签名块之后的真实 CD 位置
    eocd2 = locate_eocd(bytes(signed))
    struct.pack_into('<I', signed, eocd2 + 16, cd_off + len(block))
    signed = bytes(signed)

    final_apk = os.path.join(OUT_DIR, 'maowa-h5-assistant.apk')
    with open(final_apk, 'wb') as f:
        f.write(signed)
    print('signed:', final_apk, len(signed), 'bytes')

    # E. 校验
    verify_v2(signed)
    print('v2 signature self-verified OK')
    a = __import__('androguard.core.apk', fromlist=['APK']).APK(final_apk)
    print('androguard: is_signed_v2 =', a.is_signed_v2())
    print('package:', a.get_package(), 'targetSdk:', a.get_target_sdk_version())

    # dex 再解析验证
    zf2 = zf.ZipFile(final_apk)
    d3 = __import__('androguard.core.dex', fromlist=['DEX']).DEX(zf2.read('classes.dex'))
    print('patched dex parses OK, classes:', len(list(d3.get_classes())))


if __name__ == '__main__':
    main()
