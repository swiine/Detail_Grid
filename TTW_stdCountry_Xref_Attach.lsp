;----------------------------------------------------------------------------
;XREF ATTACH
;----------------------------------------------------------------------------
;
; - Description : Attaches AutoCAD reference files in the correct draw order.
; - Comments    : Project Number
; - Usage       : APPLOAD this file, then type ATTACHMYXREFS or ATTACHMY.
;
; - Created By  : Dave Arnold
;
;----------------------------------------------------------------------------

; Xref files, in draw order (first = bottom).
(setq *ttw-xref-list*
  '("xf-cv-00000-surv-digital-data-plan.dwg"            ; (1)
    "xf-cv-00000-surv-digital-data-splice.dwg"          ; (2)
    "xf-cv-00000-surv-plan.dwg"                         ; (3)
    "xf-cv-07000-pave-plan.dwg"                         ; (4)
    "xf-cv-00000-surv-contours-plan.dwg"                ; (5)
    "xf-cv-05000-util-exist-A-plan.dwg"                 ; (6)
    "xf-cv-05000-util-exist-B-plan.dwg"                 ; (7)
    "xf-cv-05000-util-exist-C-plan.dwg"                 ; (8)
    "xf-cv-05000-util-exist-D-plan.dwg"                 ; (9)
    "xf-cv-11000-vhcl-turning-demonstrations-plan.dwg"  ; (10)
    "xf-cv-05000-util-utilities-plan.dwg"               ; (11)
    "xf-cv-04000-ssoil-subsoil-plan.dwg"                ; (12)
    "xf-cv-01000-geom-cont-plan.dwg"                    ; (13)
    "xf-cv-00000-surv-boundary-plan.dwg"                ; (14)
    "xf-cv-01000-geom-civil3D-plan.dwg"                 ; (15)
    "xf-cv-01000-geom-autoCAD-plan.dwg"                 ; (16)
    "xf-cv-00000-lscp-plan.dwg"                         ; (17)
    "xf-cv-00000-bldg-plan.dwg"                         ; (18)
    "xf-cv-04000-strm-plan.dwg"                         ; (19)
    "xf-cv-04000-strm-id-plan.dwg"                      ; (20)
    "xf-cv-05000-util-sewer-id-plan.dwg"                ; (21)
    "xf-cv-07000-pave-jointing-plan.dwg"                ; (22)
    "xf-cv-07000-pave-id-plan.dwg"                      ; (23)
    "xf-cv-02000-envi-plan.dwg"                         ; (24)
    "xf-cv-00000-demo-plan.dwg"                         ; (25)
    "xf-cv-01000-geom-ctrl-plan.dwg"                    ; (26)
    "xf-cv-00000-gnrl-frames-plan.dwg"                  ; (27)
    "xf-cv-00000-surv-street-names-plan.dwg"            ; (28)
   )
)

(vl-load-com)

; Splits string STR at each DELIM character into a list of strings.
(defun ttw-split (str delim / pos out)
  (while (setq pos (vl-string-search delim str))
    (setq out (cons (substr str 1 pos) out)
          str (substr str (+ pos 2)))
  )
  (reverse (cons str out))
)

(defun ttw-xref-attach (/ olderr oldecho dwg parts stage dir i rel missing)
  (setq olderr  *error*
        oldecho (getvar "CMDECHO"))
  (defun *error* (msg)
    (setvar "CMDECHO" oldecho)
    (setq *error* olderr)
    (if (not (wcmatch (strcase msg) "*CANCEL*,*QUIT*"))
      (princ (strcat "\nError: " msg)))
    (princ)
  )
  (setvar "CMDECHO" 0)

  ; Drawing names look like <project>-TTW-<stage>-DR-..., e.g.
  ; 211798-TTW-01-DR-CV-07102 PAVEMENT PLAN - SHEET 2.dwg
  ; Each digit of the stage is a folder (stage 01 -> sheets\0\1\Xrefs\)
  ; and the xrefs are named <project>-TTW-<stage>-xf-cv-...
  (setq dwg   (getvar "DWGNAME")
        parts (ttw-split dwg "-"))
  (if (< (length parts) 4)
    (progn
      (princ (strcat "\nDrawing name \"" dwg "\" is not in the <project>-TTW-<stage>- format."))
      (exit)
    )
  )
  (setq stage (nth 2 parts)
        dir   "..\\..\\..\\sheets\\"
        i     1)
  (while (<= i (strlen stage))
    (setq dir (strcat dir (substr stage i 1) "\\")
          i   (1+ i))
  )
  (setq dir (strcat dir "Xrefs\\"
                    (nth 0 parts) "-" (nth 1 parts) "-" stage "-"))

  (setvar "TILEMODE" 1)
  (command "_.UCS" "_W")
  (command "_.-LAYER" "_M" "z_xref" "")

  ;----------------------------------------------------------
  ;                       Xref Detach
  ;----------------------------------------------------------
  (command "_.-XREF" "_D" "*xf*")

  ;----------------------------------------------------------
  ;                       Xref Attach
  ;----------------------------------------------------------
  (foreach xf *ttw-xref-list*
    (setq rel (strcat dir xf))
    (if (findfile (strcat (getvar "DWGPREFIX") rel))
      (command "_.-XREF" "_O" rel "0,0" "1" "1" "0")
      (setq missing (cons rel missing))
    )
  )

  (if missing
    (progn
      (princ "\nThese xrefs were not found and were skipped:")
      (foreach m (reverse missing) (princ (strcat "\n  " m)))
    )
  )

  (command "_.QSAVE")
  (setvar "CMDECHO" oldecho)
  (setq *error* olderr)
  (princ "\nXref attach complete.")
  (princ)
)

(defun c:ATTACHMYXREFS () (ttw-xref-attach))
(defun c:ATTACHMY () (ttw-xref-attach))

(princ "\nTTW Xref Attach loaded. Type ATTACHMYXREFS or ATTACHMY to run.")
(princ)
