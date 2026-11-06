process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

async function testReports() {
  const loginRes = await fetch('https://127.0.0.1:7242/Account/Login');
  const loginHtml = await loginRes.text();
  const token = loginHtml.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)[1];
  const loginCookies = loginRes.headers.getSetCookie();

  const postLogin = await fetch('https://127.0.0.1:7242/Account/Login', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
      'Cookie': loginCookies.map(c => c.split(';')[0]).join('; ')
    },
    body: new URLSearchParams({
      UserName: 'admin@selfstorage.local',
      Password: 'SecretAdminPassword123!',
      __RequestVerificationToken: token
    }).toString(),
    redirect: 'manual'
  });
  const authCookies = [...loginCookies, ...postLogin.headers.getSetCookie()].map(c => c.split(';')[0]).join('; ');

  const r = await fetch('https://127.0.0.1:7242/Reports/Reservations?StartDate=2026-12-31&EndDate=2026-01-01', {
    headers: { 'Cookie': authCookies }
  });
  console.log('Reports status:', r.status);
  const text = await r.text();
  const alert = text.match(/alert[^>]*>([\s\S]*?)<\/div>/);
  console.log('Alert in report:', alert ? alert[1].replace(/<[^>]+>/g, ' ').trim() : 'none');
}

testReports().catch(console.error);
