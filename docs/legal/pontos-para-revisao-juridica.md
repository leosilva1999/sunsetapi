# Pontos para a revisão jurídica — Termos de Uso e Política de Privacidade (versão 2)

Minutas em `termos-de-uso.txt` e `politica-de-privacidade.txt` (mesmo texto publicado na API como versão 2 de cada
documento em 2026-10-29). Foram escritas a partir do que o sistema **realmente faz** hoje; o que depende de decisão
do negócio ou de conferência jurídica está marcado com `[COLCHETES]` no texto ou listado abaixo.

## 1. Lacunas a preencher (aparecem entre colchetes no texto)

- Razão social/nome do responsável, CNPJ/CPF e endereço (controlador).
- Encarregado (DPO): nome e e-mail. Hoje o texto usa `sunsetsss.app@outlook.com` como canal geral — confirmar.
- Foro (cidade/UF).
- Provedor de hospedagem do frontend (site `sunsetsss.com`).
- Prazos de retenção marcados "a definir": logs técnicos, registros de moderação/denúncias, dados de contas banidas,
  conteúdo oculto, prazo máximo adicional dos logs de acesso.

## 2. Decisões jurídicas que o texto assume e precisam ser validadas

1. **Idade mínima**: 18+; 13–17 apenas com assistência dos responsáveis; menores de 13 proibidos. Avaliar aderência à
   LGPD art. 14 e à legislação de proteção de crianças e adolescentes em ambiente digital (ECA Digital, Lei 15.211/2025).
2. **Bases legais** (Política, item 3): execução de contrato, legítimo interesse (segurança, moderação), obrigação
   legal (logs), consentimento (geolocalização "perto de mim"), exercício regular de direitos (registros de moderação).
3. **Logs de acesso**: mínimo de 6 meses (Marco Civil, art. 15) — confirmar se o Responsável se enquadra como provedor
   de aplicação obrigado, e o prazo adicional.
4. **Transferência internacional** (Render, Google, Hostinger, AWS): o texto cita o art. 33 genericamente; definir o
   mecanismo por prestador (cláusulas-padrão, adequação, consentimento) e conferir os contratos/DPAs com operadores.
5. **Licença de conteúdo** (Termos 4.2): inclui uso em "destaques e materiais de divulgação do próprio Sunsetsss".
   Decidir se mantém esse escopo promocional.
6. **Limitação de responsabilidade e indenidade** (Termos 12): checar compatibilidade com o CDC (cláusulas abusivas).
7. **Moderação sem aviso prévio em casos graves**, revisão por e-mail sem prazo fixo (Termos 9.4): definir SLA.
8. **Conteúdo após exclusão de conta**: o que não foi apagado permanece, anonimizado como "Usuário excluído"
   (Política 10). Validar se atende ao direito de eliminação (LGPD art. 18, VI) ou se deve haver exclusão do conteúdo
   por padrão.
9. **Dados mínimos mantidos de contas banidas** (e-mail e motivo) para evitar reincidência (Termos 9.3 / Política 7).
10. **Decisões automatizadas**: o texto afirma que não há (rankings agregados; moderação feita por pessoas).

## 3. Lacunas técnicas que o texto expõe (decisões de produto/engenharia)

| Tema | Situação hoje | Sugestão |
|---|---|---|
| Aceite dos termos | O sistema **não registra** quem aceitou qual versão. | Guardar versão e data de aceite por usuário; exigir novo aceite quando a versão mudar. |
| Metadados EXIF das fotos | Não são removidos; a imagem é pública por URL. | Remover EXIF (principalmente GPS) no upload/ou no cliente. |
| Portabilidade / exportação | Não há endpoint de exportação de dados. | Atender por e-mail por ora; avaliar rota `GET /users/me/export`. |
| Exclusão de conta | Conteúdo não é ocultado por padrão (só pela moderação). | Oferecer opção "excluir também meu conteúdo" no autoatendimento. |
| Retenção | Nenhum job apaga logs, conteúdo oculto ou registros após prazo. | Implementar rotinas conforme os prazos que forem definidos. |
| Cookies / armazenamento local | Inventário real está no frontend (outro repositório). | Conferir e, se houver analytics, avaliar banner de consentimento. |
| Consentimento de geolocalização | Dado pelo navegador; não há registro no servidor. | Avaliar se basta a permissão do navegador. |

## 4. Observação sobre a publicação

Os textos já estão **no ar** como versão 2 (a versão 1 de desenvolvimento permanece no histórico). Ambos trazem, no topo,
um aviso de que estão em revisão jurídica. Após a revisão, publique a versão final com `PUT /terms` e `PUT /privacy`
(perfil Admin); cada publicação cria uma nova versão e mantém o histórico.
