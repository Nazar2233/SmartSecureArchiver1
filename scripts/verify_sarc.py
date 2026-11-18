#!/usr/bin/env python3
import sys, hashlib, hmac

def verify_volume(path: str, password: str | None = None) -> None:
    with open(path, "rb") as f:
        data = f.read()

    if len(data) < 73:
        raise ValueError(f"[E_TRUNCATED] Розмір файлу {len(data)} Б < 73 Б")
    if data[0:4] != b"SARC":
        raise ValueError(f"[E_MAGIC] Очікувалось b'SARC', отримано {data[0:4]!r}")

    version, vol_num, total_vols = data[4], data[5], data[6]
    salt, nonce = data[7:23], data[23:35]
    cipher_len = int.from_bytes(data[35:41], "little")

    if 41 + cipher_len + 32 != len(data):
        raise ValueError(f"[E_LENGTH] 41 + {cipher_len} + 32 != {len(data)}")

    print(f"[OK] {path}: SARC v{version} | Том {vol_num}/{total_vols} | CipherLen={cipher_len} Б")

    if password is not None:
        master = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, 100_000, dklen=64)
        expected = hmac.new(master[32:64], data[:-32], hashlib.sha256).digest()
        if not hmac.compare_digest(expected, data[-32:]):
            raise ValueError("[E_HMAC] Помилка автентифікації!")
        print(f"[OK] {path}: HMAC-SHA256 підтверджено.")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Використання: python scripts/verify_sarc.py <volume.arc> [password]")
        sys.exit(1)
    verify_volume(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else None)
