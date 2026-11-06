const crypto = require('crypto');

function check(hash, name) {
  const parts = hash.split('$');
  const salt = Buffer.from(parts[3], 'base64');
  const expected = Buffer.from(parts[4], 'base64');
  const candidates = [
    "TestPass123!", "SecretAdminPassword123!", "Admin@123", "Password123!",
    "@a", "123456", "admin", "admin123", "Demo123!", "Pass@word1", "SelfStorage@2026",
    "Customer@123", "Staff@123", "Manager@123", "Customer123!", "Staff123!", "Manager123!"
  ];
  for (const c of candidates) {
    const key = crypto.pbkdf2Sync(c, salt, 100000, 32, 'sha256');
    if (crypto.timingSafeEqual(expected, key)) {
      console.log(name, 'MATCH:', c);
      return;
    }
  }
  console.log(name, 'NO MATCH');
}

check("PBKDF2$SHA256$100000$f860fyp2nuCIZCGltnHPoA==$Rk9EA4vF/MvmAO+qQZaJDaAHFjNTJ+d3etMq2Unxtbs=", "administrator");
check("PBKDF2$SHA256$100000$BbvHrzX0WecDIHTElyXr5g==$TA+9Zrg6xaGwOb5aTU8PYCFJEsW+0hKPBmgrw4/eGXg=", "admin@selfstorage.local");
