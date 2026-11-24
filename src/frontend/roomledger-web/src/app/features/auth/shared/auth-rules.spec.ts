import { checkPassword, isEmail, isStrongPassword, maskEmail, stubRoleFor } from './auth-rules';

describe('auth rules', () => {
  it('checks each password rule separately', () => {
    expect(checkPassword('matkhau')).toEqual({ length: false, mixedCase: false, digit: false });
    expect(checkPassword('Phong204')).toEqual({ length: true, mixedCase: true, digit: true });
    expect(checkPassword('phong2044')).toEqual({ length: true, mixedCase: false, digit: true });
  });

  it('counts Vietnamese capitals as upper case', () => {
    expect(isStrongPassword('Đặtlại123')).toBe(true);
    expect(isStrongPassword('NHATRO36')).toBe(false);
  });

  it('validates email shape', () => {
    expect(isEmail('minhanh@vidu.vn')).toBe(true);
    expect(isEmail(' minhanh@vidu.vn ')).toBe(true);
    expect(isEmail('minhanh@vidu')).toBe(false);
    expect(isEmail('minh anh@vidu.vn')).toBe(false);
  });

  it('masks the email the code was sent to', () => {
    expect(maskEmail('minhanh@vidu.vn')).toBe('m•••••h@vidu.vn');
    expect(maskEmail('an@vidu.vn')).toBe('a•••@vidu.vn');
  });

  it('routes the temporary sign-in stub by email', () => {
    expect(stubRoleFor('lan.chunha@vidu.vn')).toBe('landlord');
    expect(stubRoleFor('CHUNHA@vidu.vn')).toBe('landlord');
    expect(stubRoleFor('minhanh@vidu.vn')).toBe('tenant');
  });
});
