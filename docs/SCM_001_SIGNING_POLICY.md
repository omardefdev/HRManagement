# SCM-001 — سياسة توقيع الـ Commits والـ Tags

## النطاق

| الطبقة | الأداة | الحالة |
|---|---|---|
| Git commits على `main` | GPG أو SSH signing | **مطلوب** |
| Git tags للإصدارات | GPG/SSH annotated signed tags | **مطلوب** |
| Container images | Cosign (موجود في `deploy.yml`) | مطلوب (BLD-001 يوسّعه) |

## سياسة المطورين

1. تفعيل التوقيع محلياً (`git config commit.gpgsign true` أو `gpg.format ssh` + `user.signingkey`).
2. رفع المفتاح العام إلى GitHub (Settings → SSH and GPG keys).
3. كل commit على PR يستهدف `main` يجب أن يظهر "Verified".
4. Tags الإصدار: `git tag -s vX.Y.Z -m "..."` ثم push tags.

## إعداد سريع (SSH signing)

```bash
git config --global gpg.format ssh
git config --global user.signingkey ~/.ssh/id_ed25519.pub
git config --global commit.gpgsign true
git config --global tag.gpgsign true
```

## التحقق

- GitHub branch protection: `required_signatures: true`
- `scripts/scm001-branch-protection-validate.ps1` يتحقق من وجود السياسة في المستودع
- الإصدارات: tag موقع يشير إلى commit موثوق يمر عبر CI الأخضر
