;;; ZEROELEV.lsp
;;; ---------------------------------------------------------------------------
;;; Moves objects to elevation 0 by setting the Z of every point to 0.
;;;
;;; Usage:
;;;   (load "ZEROELEV.lsp")
;;;   ZEROELEV
;;;
;;; Select objects, or press Enter to process everything in the drawing.
;;; Optionally also flattens the contents of block definitions (this affects
;;; every insert of those blocks; xrefs are never touched).
;;;
;;; Lines, 3D polylines, 3D faces, meshes and splines are projected flat onto
;;; Z=0. Planar objects (arcs, text, hatches, ...) that lie in a tilted plane
;;; cannot be given elevation 0 this way, so they are skipped and reported.
;;; Objects without editable points (regions, tables, images, multileaders,
;;; ...) are moved down to Z=0 when they are flat.
;;; ---------------------------------------------------------------------------

(vl-load-com)

;; Types whose geometry is edited directly through their DXF point data.
(setq ze:types
  '("LINE" "POINT" "CIRCLE" "ARC" "ELLIPSE" "LWPOLYLINE" "POLYLINE" "TEXT"
    "MTEXT" "INSERT" "ATTDEF" "SOLID" "TRACE" "3DFACE" "SPLINE" "HATCH"
    "DIMENSION" "LEADER" "RAY" "XLINE" "SHAPE" "TOLERANCE")
)

;; Types whose 210 group is only a thickness direction, not the object's plane.
(setq ze:wcs-types '("LINE" "POINT" "3DFACE" "SPLINE"))

(defun ze:dxf (code ed) (cdr (assoc code ed)))

;; T when the object's plane is parallel to the XY plane (or it has none).
(defun ze:flatnormal-p (ed / n)
  (or (not (setq n (ze:dxf 210 ed)))
      (and (equal (car n) 0.0 1e-9) (equal (cadr n) 0.0 1e-9)))
)

;; ED with the Z of every 3D point group (10-18) and the elevation (38) zeroed.
(defun ze:zero (ed)
  (mapcar '(lambda (g)
             (cond
               ((and (<= 10 (car g) 18) (listp (cdr g)) (= 3 (length (cdr g))))
                (list (car g) (cadr g) (caddr g) 0.0))
               ((= 38 (car g)) '(38 . 0.0))
               (T g)
             ))
          ed)
)

;; Type-specific clean-up after zeroing.
(defun ze:fix (ed / typ v l)
  (setq typ (ze:dxf 0 ed))
  (cond
    ;; A spline flattened out of a tilted plane now lies in the XY plane.
    ((and (= typ "SPLINE") (not (ze:flatnormal-p ed)))
     (subst '(210 0.0 0.0 1.0) (assoc 210 ed) ed))
    ;; Keep ray/xline directions unit length.
    ((and (member typ '("RAY" "XLINE"))
          (setq v (ze:dxf 11 ed))
          (> (setq l (distance '(0.0 0.0 0.0) v)) 1e-12))
     (subst (cons 11 (mapcar '(lambda (x) (/ x l)) v)) (assoc 11 ed) ed))
    (T ed)
  )
)

;; Flatten one entity's data: 1 = changed, 0 = already flat, -1 = failed.
(defun ze:mod (ed / new)
  (setq new (ze:fix (ze:zero ed)))
  (cond
    ((equal new ed) 0)
    ((entmod new) 1)
    (T -1)
  )
)

(defun ze:result (codes)
  (cond
    ((member -1 codes) 'failed)
    ((member 1 codes) 'changed)
    (T 'same)
  )
)

;; Header entity plus its VERTEX or ATTRIB subentities.
(defun ze:complex (en ed subtype / sub sed res)
  (setq res (list (ze:mod ed))
        sub en)
  (while (and (setq sub (entnext sub))
              (= subtype (ze:dxf 0 (setq sed (entget sub)))))
    (setq res (cons (ze:mod sed) res))
  )
  (entupd en)
  (ze:result res)
)

;; Fallback for objects without editable points: move them down if flat.
(defun ze:bbox (en / obj mn mx)
  (setq obj (vlax-ename->vla-object en))
  (if (vl-catch-all-error-p
        (vl-catch-all-apply 'vla-GetBoundingBox (list obj 'mn 'mx)))
    'unsupported
    (progn
      (setq mn (vlax-safearray->list mn)
            mx (vlax-safearray->list mx))
      (cond
        ((not (equal (caddr mn) (caddr mx) 1e-6)) 'unsupported)
        ((equal (caddr mn) 0.0 1e-9) 'same)
        ((vl-catch-all-error-p
           (vl-catch-all-apply 'vla-Move
             (list obj (vlax-3d-point 0.0 0.0 0.0)
                       (vlax-3d-point 0.0 0.0 (- (caddr mn)))))))
         'failed)
        (T 'changed)
      )
    )
  )
)

;; Flatten one entity. Returns changed, same, failed, tilted, unsupported,
;; or nil for objects that are ignored.
(defun ze:entity (en / ed typ)
  (setq ed  (entget en)
        typ (ze:dxf 0 ed))
  (cond
    ((member typ '("VIEWPORT" "VERTEX" "ATTRIB" "SEQEND")) nil)
    ((and (member typ ze:types)
          (not (member typ ze:wcs-types))
          (not (ze:flatnormal-p ed)))
     'tilted)
    ((= typ "POLYLINE") (ze:complex en ed "VERTEX"))
    ((and (= typ "INSERT") (= 1 (ze:dxf 66 ed))) (ze:complex en ed "ATTRIB"))
    ((member typ ze:types) (ze:result (list (ze:mod ed))))
    (T (ze:bbox en))
  )
)

;; Process EN and record the outcome in ze-log (local to c:ZEROELEV).
(defun ze:do (en / st)
  (if (setq st (ze:entity en))
    (setq ze-log (cons (cons st (ze:dxf 0 (entget en))) ze-log))
  )
)

;; Flatten the contents and base points of all local block definitions.
(defun ze:blocks (doc / rec name p en)
  (while (setq rec (tblnext "BLOCK" (not rec)))
    (setq name (ze:dxf 2 rec))
    ;; Skip xrefs (4), xref-dependent (16) and resolved xrefs (32), and layouts.
    (if (and (zerop (logand (ze:dxf 70 rec) 52))
             (not (wcmatch (strcase name) "`*MODEL_SPACE,`*PAPER_SPACE*")))
      (progn
        (setq p (ze:dxf 10 rec))
        (if (not (equal (caddr p) 0.0 1e-12))
          (vl-catch-all-apply 'vla-put-Origin
            (list (vla-item (vla-get-Blocks doc) name)
                  (vlax-3d-point (car p) (cadr p) 0.0)))
        )
        (setq en (ze:dxf -2 rec))
        (while en
          (ze:do en)
          (setq en (entnext en))
        )
      )
    )
  )
)

(defun ze:count (st log)
  (length (vl-remove-if-not '(lambda (x) (eq (car x) st)) log))
)

;; Comma-separated list of the distinct object types recorded with status ST.
(defun ze:types-of (st log / r)
  (foreach x log
    (if (and (eq (car x) st) (not (member (cdr x) r)))
      (setq r (cons (cdr x) r))
    )
  )
  (if r
    (substr (apply 'strcat (mapcar '(lambda (s) (strcat ", " s)) (reverse r))) 3)
    ""
  )
)

(defun ze:report (log / n)
  (princ (strcat "\nMoved to elevation 0: " (itoa (ze:count 'changed log))
                 "   Already at 0: " (itoa (ze:count 'same log))))
  (if (> (setq n (ze:count 'failed log)) 0)
    (princ (strcat "\nFailed: " (itoa n) " (" (ze:types-of 'failed log)
                   ") - check for locked layers."))
  )
  (if (> (setq n (ze:count 'tilted log)) 0)
    (princ (strcat "\nSkipped, in a tilted plane: " (itoa n)
                   " (" (ze:types-of 'tilted log) ")"))
  )
  (if (> (setq n (ze:count 'unsupported log)) 0)
    (princ (strcat "\nSkipped, not supported or not flat: " (itoa n)
                   " (" (ze:types-of 'unsupported log) ")"))
  )
)

;; --- Command ---------------------------------------------------------------

(defun c:ZEROELEV (/ *error* doc ss blk i ze-log)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))
  (defun *error* (msg)
    (vla-EndUndoMark doc)
    (if (not (wcmatch (strcase msg) "*CANCEL*,*QUIT*,*EXIT*"))
      (princ (strcat "\nError: " msg))
    )
    (princ)
  )
  (princ "\nSelect objects to move to elevation 0 <All>: ")
  (setq ss (cond ((ssget)) ((ssget "_X"))))
  (initget "Yes No")
  (setq blk (cond ((getkword "\nAlso flatten block definitions? [Yes/No] <No>: ")) ("No")))
  (vla-StartUndoMark doc)
  (if ss
    (progn
      (setq i 0)
      (repeat (sslength ss)
        (ze:do (ssname ss i))
        (setq i (1+ i))
      )
    )
  )
  (if (= blk "Yes")
    (progn
      (ze:blocks doc)
      (vla-Regen doc acAllViewports)
    )
  )
  (setvar "ELEVATION" 0.0)
  (vla-EndUndoMark doc)
  (ze:report ze-log)
  (princ)
)

(princ "\nZEROELEV loaded. Type ZEROELEV to move objects to elevation 0.")
(princ)
