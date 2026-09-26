# Política de segurança

## Relatar uma vulnerabilidade

Não abra issue pública para falhas exploráveis (ex.: escrita fora do destino na extração, execução de conteúdo,
vazamento de senha). Enquanto não houver canal privado configurado no repositório, entre em contato com os mantenedores
por mensagem privada e inclua: versão/commit, Windows, passos mínimos e, se possível, um compactado de teste **sem dados
reais**. Resposta inicial pretendida: 7 dias.

## Escopo

Tratamos como vulnerabilidade: gravação fora da pasta autorizada, seguir link/junction do compactado ou do destino,
sobrescrita sem confirmação, execução automática de arquivos, senha persistida/registrada, limites de recursos
contornáveis. Ver o modelo de ameaça e **os limites declarados** em `docs/security-model.md`.

## Fora do escopo

Ataques que exigem outro processo malicioso já executando com os mesmos privilégios (documentado como limitação),
conteúdo malicioso que o usuário abre por fora do aplicativo, e detecção de malware (o app não é antivírus).
