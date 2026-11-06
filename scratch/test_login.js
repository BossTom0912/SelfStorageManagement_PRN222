const https = require('https');

process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

async function testLogin(email, password) {
  const getRes = await fetch('https://127.0.0.1:7242/Account/Login', { redirect: 'manual' });
  const cookies = getRes.headers.getSetCookie();
  const html = await getRes.text();
  const match = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/);
  const token = match[1];
  const cookieHeader = cookies.map(c => c.split(';')[0]).join('; ');

  const body = new URLSearchParams({
    UserName: email,
    Password: password,
    __RequestVerificationToken: token,
    RememberMe: 'false'
  });

  const postRes = await fetch('https://127.0.0.1:7242/Account/Login', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
      'Cookie': cookieHeader
    },
    body: body.toString(),
    redirect: 'manual'
  });

  console.log('Login status:', postRes.status);
  console.log('Location:', postRes.headers.get('location'));
  const postCookies = postRes.headers.getSetCookie();
  console.log('Post cookies count:', postCookies.length);
  const authCookie = postCookies.find(c => c.includes('SelfStoragePRN222.Web.Auth'));
  console.log('Has Auth Cookie:', !!authCookie);

  // Combine cookies
  const allCookies = [...cookies, ...postCookies].map(c => c.split(';')[0]).join('; ');

  // 3. GET /StorageUnits/Index
  const indexRes = await fetch('https://127.0.0.1:7242/StorageUnits/Index', {
    headers: {
      'Cookie': allCookies
    },
    redirect: 'manual'
  });
  console.log('StorageUnits status:', indexRes.status);
  const indexHtml = await indexRes.text();
  console.log('Contains Quản trị danh mục ô kho:', indexHtml.includes('Quản trị danh mục ô kho'));
  console.log('Contains table:', indexHtml.includes('table'));
}

testLogin('admin@selfstorage.local', 'SecretAdminPassword123!').catch(console.error);
