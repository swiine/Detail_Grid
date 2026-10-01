;----------------------------------------------------------------------------
;XREF ATTACH
;----------------------------------------------------------------------------
;
; - Description : Attaches AutoCAD reference files in the correct draw order.
; - Comments    : Project Number
; - Usage       : APPLOAD this file, then type XATTACH or XA.
;
; - Created By  : Dave Arnold
;
;----------------------------------------------------------------------------

; XATTACH and XA are built-in AutoCAD names (XA is the acad.pgp alias for
; XATTACH), so the native command is undefined here so the LISP version runs.
; Type REDEFINE XATTACH to get the original back.
(if (getcname "XATTACH")
  (command "_.UNDEFINE" "XATTACH")
)

; Xref files, in draw order (first = bottom).
(setq *ttw-xref-list*
  '("xf-cv-00000-surv-digital-data-plan.dwg"            ; (1)
    "xf-cv-00000-surv-digital-data-splice.dwg"          ; (2)
    "xf-cv-00000-surv-plan.dwg"                         ; (3)
    "xf-cv-03000-pave-plan.dwg"                         ; (4)
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
    "xf-cv-04000-strm-stormwater-plan.dwg"              ; (19)
    "xf-cv-04000-strm-stormwater-id-plan.dwg"           ; (20)
    "xf-cv-05000-util-sewer-id-plan.dwg"                ; (21)
    "xf-cv-03000-pave-jointing-plan.dwg"                ; (22)
    "xf-cv-03000-pave-id-plan.dwg"                      ; (23)
    "xf-cv-09000-envi-plan.dwg"                         ; (24)
    "xf-cv-10000-demo-plan.dwg"                         ; (25)
    "xf-cv-01000-geom-ctrl-plan.dwg"                    ; (26)
    "xf-cv-00000-gnrl-frames-plan.dwg"                  ; (27)
    "xf-cv-00000-surv-street-names-plan.dwg"            ; (28)
   )
)

(defun ttw-xref-attach (/ olderr oldecho dwg dir rel missing)
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
  (setq dwg (getvar "DWGNAME")
        dir (strcat "..\\..\\..\\sheets\\"
                    (substr dwg 12 1) "\\"
                    (substr dwg 13 1) "\\Xrefs\\"
                    (substr dwg 1 14)))
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

(defun c:XATTACH () (ttw-xref-attach))
(defun c:XA () (ttw-xref-attach))

(princ "\nTTW Xref Attach loaded. Type XATTACH or XA to run.")
(princ)
